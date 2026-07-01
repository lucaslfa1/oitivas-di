using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

/// <summary>
/// Servico de transcricao de audio do Sentinel usando a API Azure Speech "Fast Transcription" (sincrona, REST).
/// </summary>
/// <remarks>
/// COMO funciona (visao geral do modulo):
/// Este servico e o ponto de entrada para converter um arquivo de audio (ex.: gravacao de oitiva)
/// em texto rotulado por interlocutor (Operador vs. Motorista/Entrevistado), pronto para o laudo forense.
///
/// Pipeline de ponta a ponta:
/// 1. <see cref="TranscreverAsync"/> monta um POST multipart (audio + "definition" JSON) e chama a Azure.
/// 2. A "definition" (<see cref="ConstruirDefinitionJson"/>) liga diarizacao (separacao de falantes) e
///    injeta uma phraseList para enviesar o reconhecimento a termos do dominio (placas, jargao, nomes).
/// 3. <see cref="FormatarTranscricao"/> le o JSON de resposta, aplica correcoes configuradas e roda uma
///    cadeia de heuristicas (mesclagem, classificacao de speakers, rebalanceamento, suavizacao) para
///    produzir um transcript legivel no formato "[mm:ss] Speaker: texto".
///
/// IMPORTANTE: este projeto e Azure-only. Nao existe fallback para Gemini/Vertex/Central nem outro provedor;
/// toda a inteligencia de diarizacao vem da Azure + heuristicas locais em <see cref="SpeakerDetectionService"/>.
///
/// Diarizacao da Azure x heuristica local: a Azure devolve um "speaker" numerico (0,1,2...) por frase, mas
/// NAO sabe quem e Operador e quem e Entrevistado. O mapeamento desses ids para papeis humanos e feito aqui,
/// por scoring linguistico (perguntas, frases de introducao, respostas curtas, ordem de fala).
/// </remarks>
public class AzureFastTranscricaoService
{

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly AzureSpeechSettings _settings;
    private readonly TextCorrectionsWrapper _corrections;
    private readonly ILogger<AzureFastTranscricaoService> _logger;

    /// <summary>
    /// Indica se o STT da Azure pode ser usado: precisa estar habilitado E ter chave + regiao resolvidas.
    /// </summary>
    /// <remarks>
    /// COMO funciona: exige os DOIS flags de habilitacao (<c>Enabled</c> global e <c>SpeechToTextEnabled</c>
    /// especifico do STT) e que tanto a chave quanto a regiao sejam resolvidas (via settings ou variaveis de
    /// ambiente — ver <see cref="ObterSpeechKey"/> / <see cref="ObterSpeechRegion"/>). Se qualquer um faltar,
    /// o servico se considera nao configurado e <see cref="TranscreverAsync"/> lanca excecao em vez de chamar a API.
    /// </remarks>
    public bool IsConfigured =>
        _settings.Enabled &&
        _settings.SpeechToTextEnabled &&
        !string.IsNullOrWhiteSpace(ObterSpeechKey()) &&
        !string.IsNullOrWhiteSpace(ObterSpeechRegion());

    /// <summary>
    /// Injeta dependencias e configura o HttpClient (timeout longo) para transcricoes sincronas de audios grandes.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - Materializa as opcoes (<c>IOptions</c>) em instancias diretas (<c>_settings</c>, <c>_corrections</c>)
    ///   para evitar acesso repetido a <c>.Value</c>.
    /// - Define <c>Timeout = 15 minutos</c>: a Fast Transcription e sincrona (a requisicao so retorna quando o
    ///   audio inteiro foi processado), entao audios longos podem demorar; o timeout padrao do HttpClient (100s)
    ///   seria insuficiente e abortaria oitivas extensas.
    /// - Loga (em nivel Warning, para ficar visivel no startup) quantas correcoes de texto foram carregadas do
    ///   appsettings; serve como diagnostico rapido de que o dicionario de correcoes nao veio vazio.
    /// </remarks>
    /// <param name="httpClient">Cliente HTTP usado para chamar a API REST da Azure Speech.</param>
    /// <param name="configuration">Configuracao da aplicacao; fonte de fallback para chave/regiao via variaveis de ambiente.</param>
    /// <param name="settings">Configuracoes do Azure Speech (habilitacao, locale, diarizacao, phraseList etc.).</param>
    /// <param name="correctionsOptions">Dicionario de correcoes de texto (padroes -> alvo) aplicadas ao transcript.</param>
    /// <param name="logger">Logger para diagnostico do pipeline de transcricao.</param>
    public AzureFastTranscricaoService(
        HttpClient httpClient,
        IConfiguration configuration,
        IOptions<AzureSpeechSettings> settings,
        IOptions<TextCorrectionsWrapper> correctionsOptions,
        ILogger<AzureFastTranscricaoService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _settings = settings.Value;
        _corrections = correctionsOptions.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(15);
        
        _logger.LogWarning("Inicializando AzureSTT: Carregadas {Count} correcoes de texto do appsettings", 
            _corrections?.Corrections?.Count ?? 0);
    }

    /// <summary>
    /// Transcreve um audio em bytes para texto rotulado por interlocutor, usando a Azure Fast Transcription.
    /// </summary>
    /// <remarks>
    /// COMO funciona (passo a passo):
    /// 1. Valida <see cref="IsConfigured"/>; se nao configurado, falha rapido (nao gasta chamada de API).
    /// 2. Monta a URL do endpoint via <see cref="ConstruirFastTranscriptionUrl"/>.
    /// 3. Constroi um corpo <c>multipart/form-data</c> com DUAS partes, exatamente como a API exige:
    ///    - parte "audio": os bytes do arquivo, com o Content-Type vindo do <paramref name="mimeType"/> real
    ///      (o nome de arquivo "audio.wav" e apenas rotulo do form; o tipo verdadeiro vem do header).
    ///    - parte "definition": o JSON de configuracao (locale, diarizacao, phraseList) de <see cref="ConstruirDefinitionJson"/>.
    /// 4. Autentica via header <c>Ocp-Apim-Subscription-Key</c> (padrao das APIs Cognitive Services da Azure).
    /// 5. Envia o POST e LE o corpo da resposta SEMPRE (mesmo em erro), pois a Azure devolve o detalhe do erro
    ///    no proprio body — esse texto e incluido na excecao para facilitar o diagnostico.
    /// 6. Em sucesso, delega a formatacao/heuristicas a <see cref="FormatarTranscricao"/>.
    /// </remarks>
    /// <param name="audioBytes">Conteudo binario do audio a transcrever.</param>
    /// <param name="mimeType">Content-Type real do audio (ex.: "audio/wav", "audio/ogg") enviado no header da parte multipart.</param>
    /// <returns>Transcript formatado como linhas "[mm:ss] Speaker: texto", ou string vazia se nao houver fala reconhecida.</returns>
    /// <exception cref="InvalidOperationException">Quando o servico de STT da Azure nao esta configurado.</exception>
    /// <exception cref="HttpRequestException">Quando a Azure retorna status de erro; a mensagem inclui status e corpo da resposta.</exception>
    public async Task<string> TranscreverAsync(byte[] audioBytes, string mimeType)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure Speech-to-Text nao esta configurado.");
        }

        var url = ConstruirFastTranscriptionUrl();

        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        content.Add(audioContent, "audio", "audio.wav");

        var definitionJson = ConstruirDefinitionJson();
        content.Add(new StringContent(definitionJson, Encoding.UTF8, "application/json"), "definition");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", ObterSpeechKey());
        request.Content = content;

        _logger.LogInformation("Enviando requisicao Azure Speech Fast Transcription: {Url}", url);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("FALHA AZURE SPEECH STT ({Status}): {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Erro Azure Speech STT: {(int)response.StatusCode} - {responseBody}");
        }

        return FormatarTranscricao(responseBody, audioBytes.Length);
    }

    /// <summary>
    /// Monta a URL do endpoint REST de Fast Transcription para a regiao e versao de API configuradas.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - Normaliza a regiao para minusculas sem espacos porque ela vira SUBDOMINIO do host
    ///   (<c>{region}.api.cognitive.microsoft.com</c>) e DNS/URL nao toleram caixa alta ou espacos.
    /// - Usa a versao de API de <c>SpeechToTextApiVersion</c> ou, se vazia, o default <c>"2025-10-15"</c>
    ///   (versao da API Fast Transcription validada para este projeto). Travar o default evita que uma
    ///   mudanca silenciosa de versao quebre o contrato do payload/resposta.
    /// - O caminho <c>transcriptions:transcribe</c> e a operacao sincrona de transcricao rapida.
    /// </remarks>
    /// <returns>URL absoluta do endpoint de transcricao, ja com o parametro <c>api-version</c>.</returns>
    private string ConstruirFastTranscriptionUrl()
    {
        var region = ObterSpeechRegion().Trim().ToLowerInvariant();
        var apiVersion = string.IsNullOrWhiteSpace(_settings.SpeechToTextApiVersion)
            ? "2025-10-15"
            : _settings.SpeechToTextApiVersion.Trim();

        return $"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version={apiVersion}";
    }

    /// <summary>
    /// Constroi o JSON "definition" que parametriza a transcricao: idioma, diarizacao e phraseList de dominio.
    /// </summary>
    /// <remarks>
    /// COMO funciona (campos do payload):
    /// - <c>locales</c>: lista com um unico idioma (default <c>"pt-BR"</c>); a Azure usa para escolher o modelo acustico.
    /// - <c>profanityFilterMode = "None"</c>: DESLIGA a censura de palavroes. Em contexto forense o transcript
    ///   precisa ser fiel ao que foi dito (palavroes podem ser prova/contexto), entao nao se mascara nada.
    /// - <c>diarization</c>: quando habilitada, pede separacao de falantes com <c>maxSpeakers</c> limitado por
    ///   <c>Math.Clamp(..., 1, 10)</c> — 1 e o minimo logico e 10 e o teto suportado pela API; o clamp protege
    ///   contra valores de configuracao fora da faixa que a Azure rejeitaria. Quando desligada, envia apenas
    ///   <c>{ enabled = false }</c> e a resposta vem sem rotulos de speaker.
    /// - <c>phraseList</c> (opcional): so e incluida se o flag estiver ligado E houver frases (ver
    ///   <see cref="MontarPhraseList"/>). Ela enviesa o reconhecimento a termos do dominio (placas, jargao,
    ///   nomes proprios), reduzindo erros em palavras que o modelo generico erraria.
    /// - Serializa ignorando propriedades nulas (<c>WhenWritingNull</c>) para manter o JSON enxuto e nao enviar
    ///   chaves vazias que a API poderia interpretar mal.
    /// </remarks>
    /// <returns>String JSON pronta para ser enviada como a parte "definition" do multipart.</returns>
    private string ConstruirDefinitionJson()
    {
        var locale = string.IsNullOrWhiteSpace(_settings.SpeechToTextLocale)
            ? "pt-BR"
            : _settings.SpeechToTextLocale.Trim();

        var maxSpeakers = Math.Clamp(_settings.SpeechToTextMaxSpeakers, 1, 10);

        var definition = new Dictionary<string, object?>
        {
            ["locales"] = new[] { locale },
            ["profanityFilterMode"] = "None",
            ["diarization"] = _settings.SpeechToTextUseDiarization
                ? new { enabled = true, maxSpeakers }
                : new { enabled = false }
        };

        var phraseList = MontarPhraseList();
        if (_settings.SpeechToTextUsePhraseList && phraseList.Count > 0)
        {
            definition["phraseList"] = new { phrases = phraseList };
        }

        return JsonSerializer.Serialize(definition, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Reune as frases/termos de dominio que serao enviados como phraseList para enviesar o reconhecimento.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - Usa um <c>HashSet</c> com <c>OrdinalIgnoreCase</c> para DEDUPLICAR termos sem distincao de caixa
    ///   (evita mandar "Honda" e "honda" como itens separados, o que so desperdicaria o orcamento da phraseList).
    /// - Fonte 1: a lista explicita <c>SpeechToTextPhraseList</c> das configuracoes.
    /// - Fonte 2: o campo <c>Target</c> de cada correcao configurada — a forma "correta/canonica" de cada termo.
    ///   Incluir o alvo da correcao ajuda o modelo a JA reconhecer o termo certo na origem, reduzindo a
    ///   necessidade de correcao posterior por regex.
    /// - Ignora entradas em branco e faz <c>Trim</c> em cada item.
    /// </remarks>
    /// <returns>Lista deduplicada de termos para a phraseList (pode ser vazia se nada estiver configurado).</returns>
    private List<string> MontarPhraseList()
    {
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var phrase in _settings.SpeechToTextPhraseList)
        {
            if (!string.IsNullOrWhiteSpace(phrase))
            {
                phrases.Add(phrase.Trim());
            }
        }

        if (_corrections?.Corrections != null)
        {
            foreach (var correction in _corrections.Corrections)
            {
                if (!string.IsNullOrWhiteSpace(correction?.Target))
                {
                    phrases.Add(correction.Target.Trim());
                }
            }
        }

        return phrases.ToList();
    }

    /// <summary>
    /// Converte o corpo de resposta da Azure no transcript final formatado, aplicando correcoes e heuristicas.
    /// </summary>
    /// <remarks>
    /// COMO funciona (passo a passo):
    /// 1. Defesas: resposta vazia -> string vazia. Se o corpo NAO comeca com "{", trata como texto puro
    ///    (resposta nao-JSON) e apenas aplica as correcoes configuradas — um caminho de fallback robusto.
    /// 2. Caminho JSON principal usa o campo <c>phrases</c> (frases com timestamps e speaker). Se ele estiver
    ///    ausente/vazio, ha um fallback para <c>combinedPhrases</c> (texto agregado, SEM diarizacao): concatena
    ///    os textos separados por espaco. Esse fallback garante saida util quando a diarizacao nao retornou frases.
    /// 3. Para cada frase em <c>phrases</c>: extrai texto, aplica correcoes, e le <c>offsetMilliseconds</c>,
    ///    <c>durationMilliseconds</c> e <c>speaker</c> de forma defensiva (so se forem realmente numericos).
    ///    - offset/duration sao convertidos com <c>Math.Max(0, ...)</c> para nunca produzir tempo negativo;
    ///      duration vai de ms para segundos (divide por 1000).
    ///    - <c>speaker</c> ausente vira <c>-1</c> (sentinela de "sem id"), tratado depois como "nao diarizado".
    ///    - guarda tambem o texto normalizado (<c>NormalizarTexto</c>) que alimenta todo o scoring posterior.
    ///    Frases que ficam vazias apos correcao sao descartadas.
    /// 4. Cadeia de pos-processamento (ordem IMPORTA, cada etapa assume a anterior):
    ///    a. <see cref="MesclarPhrasesContiguas"/>  — junta falas coladas do mesmo speaker.
    ///    b. <see cref="ClassificarSpeakers"/>      — converte ids numericos em "Operador"/"Motorista".
    ///    c. RebalancearInterlocutoresPorTurno / SuavizarTrocaIsolada / FiltrarRunsRepetitivos /
    ///       CompactarFraseDominante                 — refinam a alternancia de turnos e limpam ruido.
    ///    d. RemoverDuplicatasContiguas -> MesclarSegmentosConsecutivos -> RemoverDuplicatasContiguas
    ///       — a remocao de duplicatas e chamada DUAS vezes de proposito: mesclar segmentos pode criar novas
    ///         adjacencias duplicadas que so aparecem apos a fusao, entao limpa-se de novo no fim.
    /// 5. Renderiza cada segmento como "[mm:ss] Speaker: texto" e loga contagem de segmentos e tamanho do audio (KB).
    /// </remarks>
    /// <param name="responseBody">Corpo bruto (JSON ou texto) retornado pela API da Azure.</param>
    /// <param name="audioBytesLength">Tamanho do audio original em bytes; usado apenas para log diagnostico (em KB).</param>
    /// <returns>Transcript formatado pronto para o laudo, ou string vazia se nao houver fala aproveitavel.</returns>
    private string FormatarTranscricao(string responseBody, int audioBytesLength)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        if (!responseBody.TrimStart().StartsWith("{"))
        {
            return AplicarCorrecoesConfiguradas(responseBody.Trim());
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("phrases", out var phrasesNode) ||
            phrasesNode.ValueKind != JsonValueKind.Array ||
            phrasesNode.GetArrayLength() == 0)
        {
            if (root.TryGetProperty("combinedPhrases", out var combinedNode) &&
                combinedNode.ValueKind == JsonValueKind.Array &&
                combinedNode.GetArrayLength() > 0)
            {
                var sbCombined = new StringBuilder();
                foreach (var p in combinedNode.EnumerateArray())
                {
                    if (p.TryGetProperty("text", out var textNode))
                    {
                        var t = AplicarCorrecoesConfiguradas((textNode.GetString() ?? string.Empty).Trim());
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            if (sbCombined.Length > 0)
                            {
                                sbCombined.Append(' ');
                            }

                            sbCombined.Append(t);
                        }
                    }
                }

                return sbCombined.ToString().Trim();
            }

            return string.Empty;
        }

        var rawPhrases = new List<RawPhrase>();
        foreach (var phrase in phrasesNode.EnumerateArray())
        {
            var texto = phrase.TryGetProperty("text", out var textNode)
                ? (textNode.GetString() ?? string.Empty).Trim()
                : string.Empty;

            texto = AplicarCorrecoesConfiguradas(texto);
            if (string.IsNullOrWhiteSpace(texto))
            {
                continue;
            }

            var offsetMs = phrase.TryGetProperty("offsetMilliseconds", out var offsetMsNode) &&
                           offsetMsNode.ValueKind == JsonValueKind.Number
                ? offsetMsNode.GetDouble()
                : 0d;

            var durationMs = phrase.TryGetProperty("durationMilliseconds", out var durationMsNode) &&
                             durationMsNode.ValueKind == JsonValueKind.Number
                ? durationMsNode.GetDouble()
                : 0d;

            var speakerId = phrase.TryGetProperty("speaker", out var speakerNode) &&
                            speakerNode.ValueKind == JsonValueKind.Number
                ? speakerNode.GetInt32()
                : -1;

            rawPhrases.Add(new RawPhrase(
                Timestamp: TimeSpan.FromMilliseconds(Math.Max(0d, offsetMs)),
                DuracaoSeconds: Math.Max(0d, durationMs / 1000d),
                SpeakerId: speakerId,
                Texto: texto,
                TextoNormalizado: SpeakerDetectionService.NormalizarTexto(texto)));
        }

        if (rawPhrases.Count == 0)
        {
            return string.Empty;
        }

        rawPhrases = MesclarPhrasesContiguas(rawPhrases);
        var segmentos = ClassificarSpeakers(rawPhrases);
        segmentos = SpeakerDetectionService.RebalancearInterlocutoresPorTurno(segmentos);
        segmentos = SpeakerDetectionService.SuavizarTrocaIsoladaDeSpeaker(segmentos);
        segmentos = SpeakerDetectionService.FiltrarRunsRepetitivos(segmentos);
        segmentos = SpeakerDetectionService.CompactarFraseDominante(segmentos);
        segmentos = SpeakerDetectionService.RemoverDuplicatasContiguas(segmentos);
        segmentos = SpeakerDetectionService.MesclarSegmentosConsecutivos(segmentos);
        segmentos = SpeakerDetectionService.RemoverDuplicatasContiguas(segmentos);

        var sb = new StringBuilder();
        foreach (var s in segmentos)
        {
            sb.AppendLine($"[{SpeakerDetectionService.FormatarTimestamp(s.Timestamp)}] {s.Speaker}: {s.Texto}");
        }

        var resultado = sb.ToString().Trim();

        _logger.LogInformation(
            "Azure Speech STT processado: {Segmentos} segmentos, {AudioKb} KB",
            segmentos.Count,
            audioBytesLength / 1024);

        return resultado;
    }

    /// <summary>
    /// Funde frases consecutivas do MESMO falante que estao temporalmente coladas, reconstruindo turnos inteiros.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - A Azure costuma fragmentar uma unica fala continua em varias "phrases". Esta etapa as reagrupa para
    ///   que cada turno de fala vire um unico segmento legivel.
    /// - Ordena por timestamp e percorre em sequencia comparando cada frase com a ULTIMA ja mesclada:
    ///   - Se o <c>SpeakerId</c> difere -> e outro falante, inicia novo segmento (nunca funde falantes distintos).
    ///   - Calcula o GAP = inicio da frase atual menos o fim da ultima (offset + duracao).
    ///   - LIMIAR de 1.6 segundos: se o silencio entre as falas for maior que 1.6s, considera-se uma nova
    ///     elocucao (pausa longa = provavel troca de turno/topico) e NAO funde. Abaixo disso, trata como a mesma
    ///     fala apenas segmentada pelo ASR e funde. O valor 1.6s e um meio-termo: curto o bastante para nao
    ///     colar respostas distintas, longo o bastante para absorver pausas naturais de respiracao/hesitacao.
    /// - Ao fundir: une os textos (<c>UnirTextos</c>), recalcula o texto normalizado e ESTENDE a duracao do
    ///   segmento ate o fim da frase incorporada (fim_atual - inicio_do_segmento), mantendo o timestamp original
    ///   de inicio. Isso preserva a janela temporal correta do turno para etapas seguintes que usam duracao.
    /// </remarks>
    /// <param name="phrases">Frases cruas extraidas da resposta (ja com correcoes aplicadas).</param>
    /// <returns>Lista de frases com turnos contiguos do mesmo falante fundidos.</returns>
    private List<RawPhrase> MesclarPhrasesContiguas(List<RawPhrase> phrases)
    {
        if (phrases.Count < 2)
        {
            return phrases;
        }

        var ordenadas = phrases
            .OrderBy(p => p.Timestamp)
            .ToList();

        var mescladas = new List<RawPhrase>(ordenadas.Count) { ordenadas[0] };

        for (var i = 1; i < ordenadas.Count; i++)
        {
            var atual = ordenadas[i];
            var ultimo = mescladas[^1];

            if (atual.SpeakerId != ultimo.SpeakerId)
            {
                mescladas.Add(atual);
                continue;
            }

            var fimUltimo = ultimo.Timestamp.TotalSeconds + ultimo.DuracaoSeconds;
            var gap = atual.Timestamp.TotalSeconds - fimUltimo;

            if (gap > 1.6d)
            {
                mescladas.Add(atual);
                continue;
            }

            var textoMesclado = SpeakerDetectionService.UnirTextos(ultimo.Texto, atual.Texto);
            mescladas[^1] = ultimo with
            {
                Texto = textoMesclado,
                TextoNormalizado = SpeakerDetectionService.NormalizarTexto(textoMesclado),
                DuracaoSeconds = Math.Max(0d, (atual.Timestamp.TotalSeconds + atual.DuracaoSeconds) - ultimo.Timestamp.TotalSeconds)
            };
        }

        return mescladas;
    }

    /// <summary>
    /// Atribui a cada frase um papel humano ("Operador" ou "Motorista/Entrevistado"), combinando o id de
    /// diarizacao da Azure com evidencias linguisticas frase a frase.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Primeiro obtem o mapa global id->papel via <see cref="MapearSpeakersPorId"/> (decisao "macro", baseada
    ///    em todas as falas de cada falante).
    /// 2. Mantem estado conversacional: <c>ultimoSpeaker</c> (default Operador, pois a oitiva normalmente comeca
    ///    pela introducao do operador) e <c>aguardandoRespostaInterlocutor</c> (true logo apos uma pergunta do
    ///    operador — sinaliza que a proxima fala provavelmente e resposta do entrevistado).
    /// 3. Para cada frase calcula scores linguisticos:
    ///    - <c>scoreOperador</c>/<c>scoreInterlocutor</c> vem de <c>PontuarOperador</c>/<c>PontuarMotorista</c>.
    ///    - +2 no operador se a frase for pergunta/direcionamento (operador conduz a entrevista).
    ///    - +1 no interlocutor se for resposta curta (tipico de quem responde, nao de quem conduz).
    /// 4. Define dois "gatilhos fortes" (assimetricos de proposito):
    ///    - <c>operadorForte</c>: scoreOperador >= scoreInterlocutor+3 E >= 4 (margem alta -> evidencia robusta).
    ///    - <c>interlocutorForte</c>: scoreInterlocutor >= scoreOperador+2 E >= 2 (limiar menor porque sinais de
    ///      entrevistado costumam ser mais fracos/curtos; exigir margem alta perderia muitas respostas legitimas).
    /// 5. Decisao por frase:
    ///    - Se a frase tem id mapeado, usa o papel do mapa como base, mas aplica uma "regra suave de excecao":
    ///      um id rotulado Operador pode ser rebaixado a Motorista SE houver evidencia forte de interlocutor E a
    ///      frase NAO for pergunta; e um id Motorista pode ser promovido a Operador se houver evidencia forte de
    ///      operador. Isso corrige erros pontuais de diarizacao sem descartar o sinal global.
    ///    - Se NAO ha id (frase nao diarizada, id -1), cai na heuristica sequencial <c>DetectarSpeaker</c>, que
    ///      usa o ultimo falante e o estado de "aguardando resposta".
    /// 6. Atualiza o estado: pergunta do operador liga <c>aguardandoRespostaInterlocutor</c>; fala do motorista o desliga.
    /// 7. Por fim aplica <see cref="RebalancearTurnosSuave"/> para corrigir blocos onde varios "Operador"
    ///    seguidos deveriam ser respostas do entrevistado.
    /// </remarks>
    /// <param name="phrases">Frases (ja mescladas) com texto, texto normalizado, timestamp, duracao e SpeakerId.</param>
    /// <returns>Lista de segmentos formatados com o papel humano resolvido em cada um.</returns>
    private List<SegmentoFormatado> ClassificarSpeakers(List<RawPhrase> phrases)
    {
        var speakerMap = MapearSpeakersPorId(phrases);
        var segmentos = new List<SegmentoFormatado>(phrases.Count);

        var ultimoSpeaker = SpeakerDetectionService.SpeakerOperador;
        var aguardandoRespostaInterlocutor = false;

        foreach (var phrase in phrases)
        {
            var scoreOperador = SpeakerDetectionService.PontuarOperador(phrase.TextoNormalizado);
            var scoreInterlocutor = SpeakerDetectionService.PontuarMotorista(phrase.TextoNormalizado);

            if (SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(phrase.TextoNormalizado))
            {
                scoreOperador += 2;
            }

            if (SpeakerDetectionService.EhRespostaCurtaMotorista(phrase.TextoNormalizado))
            {
                scoreInterlocutor += 1;
            }

            var operadorForte = scoreOperador >= scoreInterlocutor + 3 && scoreOperador >= 4;
            var interlocutorForte = scoreInterlocutor >= scoreOperador + 2 && scoreInterlocutor >= 2;

            string speaker;

            if (phrase.SpeakerId >= 0 && speakerMap.TryGetValue(phrase.SpeakerId, out var mappedSpeaker))
            {
                speaker = mappedSpeaker;

                // Regra suave: permite excecao por frase quando evidencias sao fortes.
                if (speaker == SpeakerDetectionService.SpeakerOperador &&
                    interlocutorForte &&
                    !SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(phrase.TextoNormalizado))
                {
                    speaker = SpeakerDetectionService.SpeakerMotorista;
                }
                else if (speaker == SpeakerDetectionService.SpeakerMotorista && operadorForte)
                {
                    speaker = SpeakerDetectionService.SpeakerOperador;
                }
            }
            else
            {
                speaker = SpeakerDetectionService.DetectarSpeaker(phrase.TextoNormalizado, ultimoSpeaker, aguardandoRespostaInterlocutor);
            }

            if (speaker == SpeakerDetectionService.SpeakerOperador && SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(phrase.TextoNormalizado))
            {
                aguardandoRespostaInterlocutor = true;
            }
            else if (speaker == SpeakerDetectionService.SpeakerMotorista)
            {
                aguardandoRespostaInterlocutor = false;
            }

            segmentos.Add(new SegmentoFormatado(
                Timestamp: phrase.Timestamp,
                Speaker: speaker,
                Texto: phrase.Texto,
                TextoNormalizado: phrase.TextoNormalizado,
                DuracaoSeconds: phrase.DuracaoSeconds));

            ultimoSpeaker = speaker;
        }

        return RebalancearTurnosSuave(segmentos);
    }

    /// <summary>
    /// Decide, no nivel "macro", qual id de diarizacao da Azure corresponde ao Operador e quais ao Motorista,
    /// agregando evidencias de TODAS as falas de cada id.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Coleta os ids distintos (>= 0). Sem ids -> mapa vazio (a classificacao caira na heuristica por frase).
    /// 2. Acumula, por id, um <c>SpeakerStats</c> somando ao longo de todas as falas daquele falante:
    ///    - <c>ScoreOperador</c>/<c>ScoreInterlocutor</c> (pontuacao linguistica base);
    ///    - +2 operador e +1 em <c>Perguntas</c> quando a frase e pergunta/direcionamento;
    ///    - +1 interlocutor para resposta curta;
    ///    - +2 operador e +1 em <c>IntroOperador</c> quando ha frase de introducao do operador
    ///      (ex.: apresentacao/identificacao tipica de quem conduz a oitiva);
    ///    - <c>PrimeiraFalaSegundos</c> = menor timestamp em que o id aparece (quem fala primeiro tende a ser o operador).
    /// 3. Calcula um <c>ScoreConfiancaOperador</c> por id ponderando os sinais:
    ///       (ScoreOperador - ScoreInterlocutor)      // saldo liquido de evidencia de operador
    ///     + Perguntas * 2                            // conduzir por perguntas e o sinal mais caracteristico do operador
    ///     + IntroOperador * 3                        // frase de introducao e o sinal de MAIOR peso (quase assinatura do operador)
    ///     + (PrimeiraFalaSegundos <= 25 ? 2 : 0)     // bonus se comecou a falar nos primeiros 25s (abertura da oitiva)
    ///    Os pesos 2/3 e o bonus 2 refletem a confiabilidade relativa de cada pista; 25s e a janela tipica em que
    ///    o operador faz a abertura/identificacao antes de o entrevistado responder.
    /// 4. Ordena por confianca desc. (desempate por menor id, deterministico).
    /// 5. Resolucao:
    ///    - 1 unico id: e Operador SO se a confianca >= 4 (limiar minimo para nao rotular ruido como operador);
    ///      caso contrario marca Motorista.
    ///    - 2+ ids: o topo e considerado Operador "confiavel" se QUALQUER um valer:
    ///        (a) diferenca para o 2o >= 3  (lideranca folgada);
    ///        (b) tem pelo menos 2 perguntas a mais que o 2o (conduz claramente mais);
    ///        (c) tem qualquer frase de introducao (IntroOperador > 0, sinal quase definitivo).
    ///      Sem confianca forte: ainda elege o melhor como Operador SE ele tiver score > 0 (alguma evidencia);
    ///      se o score for 0 ou negativo (nenhuma evidencia real), marca TODOS como Motorista e delega a decisao
    ///      a heuristica por frase, evitando "chutar" um operador sem base.
    ///    - Definido o Operador, todos os demais ids viram Motorista (modelo de 1 operador vs. N entrevistados).
    /// </remarks>
    /// <param name="phrases">Frases com SpeakerId, texto normalizado e timestamps.</param>
    /// <returns>Mapa id-de-diarizacao -> papel ("Operador"/"Motorista"); vazio se nao houver ids diarizados.</returns>
    private Dictionary<int, string> MapearSpeakersPorId(List<RawPhrase> phrases)
    {
        var ids = phrases
            .Where(p => p.SpeakerId >= 0)
            .Select(p => p.SpeakerId)
            .Distinct()
            .ToList();

        var mapa = new Dictionary<int, string>();
        if (ids.Count == 0)
        {
            return mapa;
        }

        var statsById = new Dictionary<int, SpeakerStats>();
        foreach (var id in ids)
        {
            statsById[id] = new SpeakerStats(
                PrimeiraFalaSegundos: double.MaxValue,
                ScoreOperador: 0,
                ScoreInterlocutor: 0,
                Perguntas: 0,
                IntroOperador: 0);
        }

        foreach (var phrase in phrases)
        {
            if (phrase.SpeakerId < 0)
            {
                continue;
            }

            var atual = statsById[phrase.SpeakerId];
            var scoreOperador = SpeakerDetectionService.PontuarOperador(phrase.TextoNormalizado);
            var scoreInterlocutor = SpeakerDetectionService.PontuarMotorista(phrase.TextoNormalizado);
            var perguntas = 0;
            var introOperador = 0;

            if (SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(phrase.TextoNormalizado))
            {
                scoreOperador += 2;
                perguntas++;
            }

            if (SpeakerDetectionService.EhRespostaCurtaMotorista(phrase.TextoNormalizado))
            {
                scoreInterlocutor += 1;
            }

            if (SpeakerDetectionService.TemIntroOperador(phrase.TextoNormalizado))
            {
                introOperador = 1;
                scoreOperador += 2;
            }

            statsById[phrase.SpeakerId] = atual with
            {
                PrimeiraFalaSegundos = Math.Min(atual.PrimeiraFalaSegundos, phrase.Timestamp.TotalSeconds),
                ScoreOperador = atual.ScoreOperador + scoreOperador,
                ScoreInterlocutor = atual.ScoreInterlocutor + scoreInterlocutor,
                Perguntas = atual.Perguntas + perguntas,
                IntroOperador = atual.IntroOperador + introOperador
            };
        }

        var ranking = statsById
            .Select(x => new
            {
                SpeakerId = x.Key,
                Stats = x.Value,
                ScoreConfiancaOperador =
                    (x.Value.ScoreOperador - x.Value.ScoreInterlocutor) +
                    (x.Value.Perguntas * 2) +
                    (x.Value.IntroOperador * 3) +
                    (x.Value.PrimeiraFalaSegundos <= 25d ? 2 : 0)
            })
            .OrderByDescending(x => x.ScoreConfiancaOperador)
            .ThenBy(x => x.SpeakerId)
            .ToList();

        if (ranking.Count == 1)
        {
            var unico = ranking[0];
            var operadorProvavel = unico.ScoreConfiancaOperador >= 4;
            mapa[unico.SpeakerId] = operadorProvavel ? SpeakerDetectionService.SpeakerOperador : SpeakerDetectionService.SpeakerMotorista;
            return mapa;
        }

        var melhor = ranking[0];
        var segundo = ranking[1];
        var diferencaTop = melhor.ScoreConfiancaOperador - segundo.ScoreConfiancaOperador;
        var operadorConfiavel =
            diferencaTop >= 3 ||
            melhor.Stats.Perguntas >= segundo.Stats.Perguntas + 2 ||
            melhor.Stats.IntroOperador > 0;

        // Sem confiança forte, ainda atribui o melhor candidato como Operador,
        // mas apenas se tiver alguma evidência mínima (score > 0).
        if (!operadorConfiavel)
        {
            if (melhor.ScoreConfiancaOperador > 0)
            {
                // Mantém o melhor como Operador mesmo sem confiança alta
                mapa[melhor.SpeakerId] = SpeakerDetectionService.SpeakerOperador;
                foreach (var id in ids)
                {
                    if (id != melhor.SpeakerId)
                        mapa[id] = SpeakerDetectionService.SpeakerMotorista;
                }

                return mapa;
            }

            // Score zero ou negativo: genuinamente sem evidência, delega para heurística por frase
            foreach (var id in ids)
            {
                mapa[id] = SpeakerDetectionService.SpeakerMotorista;
            }

            return mapa;
        }

        mapa[melhor.SpeakerId] = SpeakerDetectionService.SpeakerOperador;
        foreach (var id in ids)
        {
            if (id == melhor.SpeakerId)
            {
                continue;
            }

            mapa[id] = SpeakerDetectionService.SpeakerMotorista;
        }

        return mapa;
    }

    /// <summary>
    /// Corrige, de forma conservadora, blocos em que falas atribuidas ao Operador logo apos uma pergunta dele
    /// sao, na verdade, a resposta do entrevistado.
    /// </summary>
    /// <remarks>
    /// COMO funciona (heuristica "pergunta -> resposta"):
    /// - Para cada segmento que e do Operador E e uma pergunta/direcionamento, olha as PROXIMAS falas como
    ///   candidatas a serem a resposta do entrevistado.
    /// - Janela de busca limitada a no maximo 3 segmentos a frente (<c>j <= i + 3</c>) E a 25 segundos
    ///   (<c>delta > 25</c> interrompe): a resposta a uma pergunta vem logo depois; alem dessa janela o vinculo
    ///   pergunta-resposta deixa de ser confiavel. Os mesmos 25s da janela de abertura aparecem aqui como
    ///   horizonte de proximidade temporal de um par pergunta/resposta.
    /// - Para cada candidato recalcula scores (mesmas regras: +2 pergunta no operador, +1 resposta curta no
    ///   interlocutor) e define <c>operadorForte</c> = scoreOperador >= scoreInterlocutor+3 E >= 4. Se o candidato
    ///   for fortemente operador, ABORTA o rebalanceamento desse bloco (e mesmo o operador falando seguido).
    /// - Rebaixamento conservador: so muda Operador -> Motorista se o candidato NAO tiver indicador forte de
    ///   operador (<c>TemIndicadorOperadorForte</c>). Assim nunca rebaixa uma fala que claramente e do operador.
    /// - Criterios de PARADA do bloco (encerra a varredura apos processar o candidato):
    ///     texto normalizado com >= 30 caracteres OU duracao >= 2.2s.
    ///   A ideia: a resposta direta a uma pergunta tende a ser curta; uma fala longa (>= 30 chars) ou demorada
    ///   (>= 2.2s) provavelmente ja e um novo turno substantivo (possivelmente o operador retomando), entao
    ///   para-se para nao reclassificar falas que nao fazem parte da resposta imediata.
    /// - So roda com >= 3 segmentos (abaixo disso nao ha padrao de turno relevante).
    /// </remarks>
    /// <param name="segmentos">Segmentos ja classificados em papel humano.</param>
    /// <returns>Lista de segmentos com possiveis correcoes de Operador -> Motorista nas respostas imediatas.</returns>
    private static List<SegmentoFormatado> RebalancearTurnosSuave(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 3)
        {
            return segmentos;
        }

        var ajustados = new List<SegmentoFormatado>(segmentos);

        for (var i = 0; i < ajustados.Count - 1; i++)
        {
            var atual = ajustados[i];
            if (atual.Speaker != SpeakerDetectionService.SpeakerOperador || !SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(atual.TextoNormalizado))
            {
                continue;
            }

            for (var j = i + 1; j < ajustados.Count && j <= i + 3; j++)
            {
                var candidato = ajustados[j];
                var delta = (candidato.Timestamp - atual.Timestamp).TotalSeconds;
                if (delta > 25d)
                {
                    break;
                }

                var scoreOperador = SpeakerDetectionService.PontuarOperador(candidato.TextoNormalizado);
                var scoreInterlocutor = SpeakerDetectionService.PontuarMotorista(candidato.TextoNormalizado);
                if (SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(candidato.TextoNormalizado))
                {
                    scoreOperador += 2;
                }

                if (SpeakerDetectionService.EhRespostaCurtaMotorista(candidato.TextoNormalizado))
                {
                    scoreInterlocutor += 1;
                }

                var operadorForte = scoreOperador >= scoreInterlocutor + 3 && scoreOperador >= 4;
                if (operadorForte)
                {
                    break;
                }

                // Só rebaixa se NÃO tiver indicador forte de operador
                if (candidato.Speaker == SpeakerDetectionService.SpeakerOperador
                    && !SpeakerDetectionService.TemIndicadorOperadorForte(candidato.TextoNormalizado))
                {
                    ajustados[j] = candidato with { Speaker = SpeakerDetectionService.SpeakerMotorista };
                }

                if (candidato.TextoNormalizado.Length >= 30 || candidato.DuracaoSeconds >= 2.2d)
                {
                    break;
                }
            }
        }

        return ajustados;
    }


    /// <summary>
    /// Aplica as correcoes de texto configuradas (substituicoes via regex) ao trecho transcrito.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - Para cada correcao, percorre seus <c>Patterns</c> (expressoes regulares) e substitui cada ocorrencia
    ///   pelo <c>Target</c> (forma canonica), com <c>IgnoreCase</c> — assim padroniza grafias/erros recorrentes do
    ///   ASR (ex.: variacoes de jargao, placas, nomes) independentemente de caixa.
    /// - As substituicoes sao acumulativas: cada padrao opera sobre o resultado do anterior.
    /// - Resiliencia: um <c>try/catch</c> envolve cada substituicao e ENGOLE a excecao de propósito — um padrao
    ///   regex invalido na configuracao nao deve derrubar a transcricao inteira; apenas aquela correcao e ignorada.
    /// - Curto-circuito inicial se nao ha texto ou nao ha correcoes, evitando trabalho desnecessario.
    /// </remarks>
    /// <param name="texto">Trecho de texto a ser corrigido.</param>
    /// <returns>Texto com as correcoes aplicadas (ou inalterado se nao houver correcoes/padroes validos).</returns>
    private string AplicarCorrecoesConfiguradas(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || _corrections?.Corrections == null || _corrections.Corrections.Count == 0)
        {
            return texto;
        }

        foreach (var correction in _corrections.Corrections)
        {
            if (correction?.Patterns == null)
            {
                continue;
            }

            foreach (var pattern in correction.Patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                try
                {
                    texto = Regex.Replace(texto, pattern, correction.Target, RegexOptions.IgnoreCase);
                }
                catch
                {
                    // Ignora padrao invalido para nao interromper a transcricao.
                }
            }
        }

        return texto;
    }

    /// <summary>
    /// Resolve a chave de assinatura do Azure Speech seguindo uma cadeia de precedencia de fontes.
    /// </summary>
    /// <remarks>
    /// COMO funciona (ordem de prioridade, primeira nao-vazia vence):
    /// 1. <c>_settings.SpeechToTextKey</c> — chave especifica do STT (mais especifica, tem prioridade).
    /// 2. Variavel de ambiente <c>AZURE_SPEECH_TO_TEXT_KEY</c> — permite injetar segredo sem versionar no appsettings
    ///    (ideal para deploy/secrets, evitando expor chave no codigo).
    /// 3. <c>_settings.SubscriptionKey</c> — chave generica de fallback compartilhada com outros recursos Speech.
    /// Essa cascata permite configurar o ambiente de varias formas mantendo o segredo fora do repositorio.
    /// </remarks>
    /// <returns>A chave de assinatura resolvida, ou string vazia se nenhuma fonte estiver preenchida.</returns>
    private string ObterSpeechKey()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SpeechToTextKey))
        {
            return _settings.SpeechToTextKey;
        }

        var envKey = _configuration["AZURE_SPEECH_TO_TEXT_KEY"];
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey;
        }

        return _settings.SubscriptionKey;
    }

    /// <summary>
    /// Resolve a regiao do Azure Speech seguindo a mesma cadeia de precedencia de fontes da chave.
    /// </summary>
    /// <remarks>
    /// COMO funciona (ordem de prioridade, primeira nao-vazia vence):
    /// 1. <c>_settings.SpeechToTextRegion</c> — regiao especifica do STT.
    /// 2. Variavel de ambiente <c>AZURE_SPEECH_TO_TEXT_REGION</c> — para configurar por ambiente sem alterar settings.
    /// 3. <c>_settings.Region</c> — regiao generica de fallback.
    /// A regiao resolvida vira subdominio do endpoint (ver <see cref="ConstruirFastTranscriptionUrl"/>), por isso
    /// precisa estar consistente com a regiao onde o recurso Speech foi provisionado, senao a chamada falha.
    /// </remarks>
    /// <returns>A regiao resolvida, ou string vazia se nenhuma fonte estiver preenchida.</returns>
    private string ObterSpeechRegion()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SpeechToTextRegion))
        {
            return _settings.SpeechToTextRegion;
        }

        var envRegion = _configuration["AZURE_SPEECH_TO_TEXT_REGION"];
        if (!string.IsNullOrWhiteSpace(envRegion))
        {
            return envRegion;
        }

        return _settings.Region;
    }
}
