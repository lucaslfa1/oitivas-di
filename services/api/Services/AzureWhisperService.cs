using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

/// <summary>
/// Cliente de transcricao de audio do Sentinel sobre o Whisper hospedado no Azure OpenAI.
/// </summary>
/// <remarks>
/// COMO funciona:
/// 1. Recebe os bytes de audio de uma oitiva/sinistro e os envia ao endpoint
///    "audio/transcriptions" de um deployment Whisper no Azure OpenAI (servico Azure-only;
///    nao ha mais integracao com Gemini/Vertex/Central).
/// 2. Solicita a resposta no formato "verbose_json", que devolve o texto quebrado em
///    segmentos com timestamps e metricas de confianca por segmento (no_speech_prob,
///    avg_logprob, compression_ratio). Essas metricas alimentam os filtros anti-alucinacao.
/// 3. Pos-processa os segmentos: aplica correcoes de vocabulario configuraveis, descarta
///    ruido/alucinacoes, atribui o interlocutor (Operador/Motorista) via
///    <see cref="SpeakerDetectionService"/> e monta uma transcricao final no formato
///    "[mm:ss] Speaker: texto" pronta para sincronizar com o player na linha do tempo.
/// O resultado e uma transcricao diarizada e limpa, usada como insumo da analise forense.
/// </remarks>
public class AzureWhisperService
{

    private readonly HttpClient _httpClient;
    private readonly AzureSpeechSettings _settings;
    private readonly TextCorrectionsWrapper _corrections;
    private readonly ILogger<AzureWhisperService> _logger;

    /// <summary>
    /// Indica se o servico esta apto a transcrever (habilitado e com credenciais minimas).
    /// </summary>
    /// <remarks>
    /// COMO funciona: so retorna verdadeiro quando o recurso esta ligado por configuracao
    /// (<c>Enabled</c>) E possui tanto a chave de assinatura quanto o endpoint preenchidos.
    /// Usado pelos chamadores como guarda para decidir se ha como acionar o Azure antes de
    /// montar a requisicao (evita chamadas fadadas a falhar por falta de credencial).
    /// </remarks>
    public bool IsConfigured =>
        _settings.Enabled &&
        !string.IsNullOrWhiteSpace(_settings.SubscriptionKey) &&
        !string.IsNullOrWhiteSpace(_settings.Endpoint);

    /// <summary>
    /// Injeta dependencias e configura o cliente HTTP usado nas chamadas ao Whisper.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Guarda o <see cref="HttpClient"/>, as configuracoes do Azure Speech/OpenAI, o
    ///    dicionario de correcoes de texto e o logger.
    /// 2. Define o timeout do HttpClient em 10 minutos, pois transcricoes de audios longos
    ///    podem demorar bem mais que o timeout padrao (100s) e estourariam por engano.
    /// 3. Emite um log de diagnostico com o estado da configuracao. So registra o COMPRIMENTO
    ///    da chave (nunca a chave em si) para nao vazar segredo no log.
    /// </remarks>
    /// <param name="httpClient">Cliente HTTP (tipicamente injetado por HttpClientFactory).</param>
    /// <param name="settings">Configuracoes do deployment Azure (endpoint, chave, deployment).</param>
    /// <param name="correctionsOptions">Regras de correcao de vocabulario aplicadas ao texto transcrito.</param>
    /// <param name="logger">Logger para diagnostico e auditoria do processamento.</param>
    public AzureWhisperService(
        HttpClient httpClient,
        IOptions<AzureSpeechSettings> settings,
        IOptions<TextCorrectionsWrapper> correctionsOptions,
        ILogger<AzureWhisperService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _corrections = correctionsOptions.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);

        _logger.LogInformation(
            "DEBUG AZURE: Enabled={Enabled}, Endpoint={Endpoint}, Deployment={Deployment}, Key={KeyLength}",
            _settings.Enabled,
            _settings.Endpoint,
            _settings.DeploymentName,
            _settings.SubscriptionKey?.Length ?? 0);
    }

    /// <summary>
    /// Transcreve um audio chamando o deployment Whisper no Azure OpenAI e devolve o texto diarizado.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline):
    /// 1. Monta a URL do endpoint a partir do <c>Endpoint</c> + nome do deployment, fixando
    ///    a API "audio/transcriptions" com <c>api-version=2024-06-01</c>. O <c>TrimEnd('/')</c>
    ///    seguido de "/" normaliza a base para evitar barras duplicadas/ausentes.
    /// 2. Empacota o audio como multipart/form-data:
    ///    - campo "file": os bytes brutos com o Content-Type do mimeType informado (nome fixo
    ///      "audio.wav" e meramente um rotulo exigido pela API; o conteudo real e o que vale);
    ///    - campo "language" = "pt": forca portugues e reduz alucinacoes de auto-deteccao;
    ///    - campo "response_format" = "verbose_json": pede segmentos + timestamps + metricas
    ///      de confianca, indispensaveis para sincronizar no player e filtrar alucinacoes.
    /// 3. Autentica via header "api-key" (padrao Azure OpenAI, diferente do Bearer do OpenAI publico).
    /// 4. Envia a requisicao; em status de erro, loga corpo + status e lanca excecao com o detalhe.
    /// 5. Em sucesso, delega a formatacao/limpeza a <see cref="FormatarTranscricaoComTimestamps"/>.
    /// </remarks>
    /// <param name="audioBytes">Conteudo binario do audio a transcrever.</param>
    /// <param name="mimeType">MIME type do audio (ex.: "audio/wav", "audio/mpeg") usado no Content-Type.</param>
    /// <returns>Transcricao formatada como "[mm:ss] Speaker: texto" por linha, ja limpa e diarizada.</returns>
    /// <exception cref="HttpRequestException">Lancada quando a API Azure retorna status de erro.</exception>
    public async Task<string> TranscreverAsync(byte[] audioBytes, string mimeType)
    {
        var baseUri = _settings.Endpoint.TrimEnd('/') + "/";
        var url = $"{baseUri}openai/deployments/{_settings.DeploymentName}/audio/transcriptions?api-version=2024-06-01";

        _logger.LogInformation("Enviando requisicao Whisper Azure OpenAI: {Url} (Deployment: {Deployment})", url, _settings.DeploymentName);

        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent("pt"), "language");
        // verbose_json traz segmentos com timestamps para sincronizacao no player.
        content.Add(new StringContent("verbose_json"), "response_format");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _settings.SubscriptionKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("FALHA WHISPER AZURE ({Status}): {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Erro API Azure: {(int)response.StatusCode} - {responseBody}");
        }

        return FormatarTranscricaoComTimestamps(responseBody, audioBytes.Length);
    }

    /// <summary>
    /// Converte o corpo "verbose_json" do Whisper em uma transcricao diarizada e limpa.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Curto-circuitos de entrada:
    ///    - corpo vazio retorna string vazia;
    ///    - se o corpo nao comeca com "{" (nao e JSON), trata como texto puro e apenas aplica
    ///      as correcoes de vocabulario (fallback para deployments que ignoram verbose_json).
    /// 2. Faz o parse do JSON e busca o array "segments". Se nao houver segmentos (ex.: audio
    ///    sem fala), tenta usar o campo "text" agregado; se nem isso existir, retorna vazio.
    /// 3. Percorre cada segmento mantendo estado entre iteracoes para diarizacao:
    ///    - <c>ultimoSpeaker</c>: ultimo interlocutor detectado (inicia como Operador, que
    ///      tipicamente abre a oitiva conduzindo a conversa);
    ///    - <c>expectativaRespostaMotoristaAte</c>: instante (em segundos) ate o qual se espera
    ///      uma resposta do Motorista apos uma pergunta do Operador (-1 = sem expectativa ativa).
    /// 4. Por segmento: extrai o texto, aplica correcoes, pula vazios, normaliza o texto e le as
    ///    metricas de confianca (no_speech_prob, avg_logprob, compression_ratio) com defaults
    ///    seguros (0) quando ausentes/nao-numericas.
    /// 5. Aplica os filtros de alucinacao em duas etapas: primeiro o overload "barato" (texto +
    ///    no_speech_prob + avg_logprob) antes de calcular tempos; depois, ja com start/end/duracao,
    ///    o overload completo (acrescenta compression_ratio, duracao e startSeconds). Segmentos
    ///    reprovados sao contados em <c>segmentosDescartados</c> e ignorados.
    /// 6. Diarizacao: calcula o timestamp inicial, verifica se ainda esta na janela de espera de
    ///    resposta do Motorista e chama <see cref="SpeakerDetectionService.DetectarSpeaker"/>.
    ///    - Se um Operador faz pergunta/direcionamento, abre uma janela de ~22s (heuristica para a
    ///      duracao tipica de uma resposta) durante a qual o proximo turno tende a ser do Motorista.
    ///    - Se o Motorista fala sem ser pergunta, fecha a janela (-1), pois a resposta ja ocorreu.
    /// 7. Acumula os <see cref="SegmentoFormatado"/> validos e, ao final, aplica em cadeia os
    ///    pos-processadores do <see cref="SpeakerDetectionService"/> (rebalanceamento de turnos,
    ///    suavizacao de troca isolada de speaker, filtragem de runs repetitivos, compactacao de
    ///    frase dominante, remocao de duplicatas e mesclagem de segmentos contiguos). A remocao de
    ///    duplicatas roda duas vezes — antes e depois da mesclagem — porque a mesclagem pode criar
    ///    novas adjacencias duplicadas.
    /// 8. Monta o texto final ("[mm:ss] Speaker: texto" por linha), loga estatisticas de
    ///    aproveitamento e, se tudo resultou vazio, cai para o campo "text" agregado como ultimo recurso.
    /// </remarks>
    /// <param name="responseBody">Corpo bruto da resposta do Whisper (idealmente verbose_json).</param>
    /// <param name="audioBytesLength">Tamanho do audio em bytes, usado apenas para log de diagnostico (KB).</param>
    /// <returns>Transcricao formatada e diarizada, ou string vazia quando nao ha conteudo aproveitavel.</returns>
    private string FormatarTranscricaoComTimestamps(string responseBody, int audioBytesLength)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        // Fallback para respostas em texto puro.
        if (!responseBody.TrimStart().StartsWith("{"))
        {
            return AplicarCorrecoesConfiguradas(responseBody.Trim());
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() == 0)
        {
            // Caso o servico retorne JSON sem segmentos (ex.: audio sem fala detectada).
            if (root.TryGetProperty("text", out var textOnly))
            {
                return textOnly.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        var ultimoSpeaker = SpeakerDetectionService.SpeakerOperador;
        var expectativaRespostaMotoristaAte = -1d;
        var segmentosDescartados = 0;
        var segmentosValidos = new List<SegmentoFormatado>();

        foreach (var segment in segments.EnumerateArray())
        {
            var texto = segment.TryGetProperty("text", out var textNode)
                ? (textNode.GetString() ?? string.Empty).Trim()
                : string.Empty;

            texto = AplicarCorrecoesConfiguradas(texto);

            if (string.IsNullOrWhiteSpace(texto))
            {
                continue;
            }

            var textoNormalizado = SpeakerDetectionService.NormalizarTexto(texto);
            var noSpeechProb = segment.TryGetProperty("no_speech_prob", out var nspNode) && nspNode.ValueKind == JsonValueKind.Number
                ? nspNode.GetDouble()
                : 0d;
            var avgLogProb = segment.TryGetProperty("avg_logprob", out var alpNode) && alpNode.ValueKind == JsonValueKind.Number
                ? alpNode.GetDouble()
                : 0d;
            var compressionRatio = segment.TryGetProperty("compression_ratio", out var crNode) && crNode.ValueKind == JsonValueKind.Number
                ? crNode.GetDouble()
                : 0d;

            if (DeveDescartarSegmento(textoNormalizado, noSpeechProb, avgLogProb))
            {
                segmentosDescartados++;
                continue;
            }

            var startSeconds = segment.TryGetProperty("start", out var startNode) && startNode.ValueKind == JsonValueKind.Number
                ? startNode.GetDouble()
                : 0d;
            var endSeconds = segment.TryGetProperty("end", out var endNode) && endNode.ValueKind == JsonValueKind.Number
                ? endNode.GetDouble()
                : startSeconds;
            var duracaoSeconds = Math.Max(0d, endSeconds - startSeconds);

            if (DeveDescartarSegmento(textoNormalizado, noSpeechProb, avgLogProb, compressionRatio, duracaoSeconds, startSeconds))
            {
                segmentosDescartados++;
                continue;
            }

            var timestamp = TimeSpan.FromSeconds(Math.Max(0, startSeconds));
            var aguardandoRespostaMotorista = startSeconds <= expectativaRespostaMotoristaAte;
            var speaker = SpeakerDetectionService.DetectarSpeaker(textoNormalizado, ultimoSpeaker, aguardandoRespostaMotorista);
            ultimoSpeaker = speaker;

            if (speaker == SpeakerDetectionService.SpeakerOperador && SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(textoNormalizado))
            {
                // Apos uma pergunta do Operador, abre uma janela de ~22s (heuristica calibrada
                // para a duracao tipica de uma resposta + pausa) na qual o proximo turno tende a
                // ser do Motorista, mesmo que o texto sozinho nao deixe o speaker obvio.
                expectativaRespostaMotoristaAte = endSeconds + 22d;
            }
            else if (speaker == SpeakerDetectionService.SpeakerMotorista && !SpeakerDetectionService.EhPerguntaOuDirecionamentoOperador(textoNormalizado))
            {
                expectativaRespostaMotoristaAte = -1d;
            }

            segmentosValidos.Add(new SegmentoFormatado(timestamp, speaker, texto, textoNormalizado, duracaoSeconds));
        }

        // Filtro em camadas para reduzir alucinacoes de repeticao sem perder conteudo util.
        segmentosValidos = SpeakerDetectionService.RebalancearInterlocutoresPorTurno(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.SuavizarTrocaIsoladaDeSpeaker(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.FiltrarRunsRepetitivos(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.CompactarFraseDominante(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.RemoverDuplicatasContiguas(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.MesclarSegmentosConsecutivos(segmentosValidos);
        segmentosValidos = SpeakerDetectionService.RemoverDuplicatasContiguas(segmentosValidos);

        var sb = new StringBuilder();
        foreach (var s in segmentosValidos)
        {
            sb.AppendLine($"[{SpeakerDetectionService.FormatarTimestamp(s.Timestamp)}] {s.Speaker}: {s.Texto}");
        }

        var resultado = sb.ToString().Trim();

        _logger.LogInformation(
            "Whisper processado: {SegmentosTotal} segmentos recebidos, {SegmentosValidos} mantidos, {SegmentosDescartados} descartados, {AudioKb} KB",
            segments.GetArrayLength(),
            segmentosValidos.Count,
            segmentosDescartados,
            audioBytesLength / 1024);

        // Se por algum motivo todos os segmentos vieram vazios, cai para text.
        if (string.IsNullOrWhiteSpace(resultado) && root.TryGetProperty("text", out var textFallback))
        {
            return AplicarCorrecoesConfiguradas(textFallback.GetString()?.Trim() ?? string.Empty);
        }

        return resultado;
    }

    /// <summary>
    /// Aplica as regras de correcao de vocabulario configuradas sobre um trecho transcrito.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Retorna o texto intacto se ele for vazio ou se nao houver regras configuradas
    ///    (curto-circuitos para evitar trabalho desnecessario).
    /// 2. Para cada correcao, percorre seus padroes regex e substitui as ocorrencias pelo
    ///    <c>Target</c>, ignorando maiusculas/minusculas (<c>RegexOptions.IgnoreCase</c>).
    /// 3. Padroes vazios sao ignorados; um padrao regex invalido e capturado e silenciado
    ///    (try/catch) para que uma regra mal configurada nao derrube a transcricao inteira —
    ///    a correcao falha localmente, mas o restante do texto segue normalmente.
    /// Usado para normalizar termos do dominio (nomes, jargoes de sinistro) que o Whisper
    /// costuma grafar de forma inconsistente.
    /// </remarks>
    /// <param name="texto">Trecho de texto transcrito a ser corrigido.</param>
    /// <returns>Texto com as substituicoes aplicadas (ou o original se nao houver regras aplicaveis).</returns>
    private string AplicarCorrecoesConfiguradas(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return texto;
        }

        if (_corrections?.Corrections == null || _corrections.Corrections.Count == 0)
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
                    // Ignora padrao invalido para nao quebrar a transcricao.
                }
            }
        }

        return texto;
    }

    /// <summary>
    /// Atalho interno que delega a normalizacao de texto ao <see cref="SpeakerDetectionService"/>.
    /// </summary>
    /// <remarks>
    /// COMO funciona: apenas repassa a chamada para
    /// <see cref="SpeakerDetectionService.NormalizarTexto"/>, mantendo a regra de normalizacao
    /// (caixa, acentos, pontuacao) centralizada em um unico lugar e reaproveitavel dentro desta classe.
    /// </remarks>
    /// <param name="texto">Texto a normalizar.</param>
    /// <returns>Texto normalizado conforme as regras do <see cref="SpeakerDetectionService"/>.</returns>
    private static string NormalizarTexto(string texto)
    {
        return SpeakerDetectionService.NormalizarTexto(texto);
    }

    /// <summary>
    /// Filtro basico de alucinacao: decide se um segmento deve ser descartado por texto/confianca.
    /// </summary>
    /// <remarks>
    /// COMO funciona (significado dos limiares):
    /// 1. Texto vazio apos normalizacao: descarta (nao ha conteudo util).
    /// 2. <c>no_speech_prob &gt;= 0.95</c> E <c>avg_logprob &lt;= -1.0</c>: descarta.
    ///    - no_speech_prob ~1.0 significa que o modelo esta quase certo de que NAO ha fala ali;
    ///    - avg_logprob muito negativo (&lt;= -1.0) indica baixissima confianca media nos tokens.
    ///    A combinacao captura o caso classico do Whisper "inventar" texto sobre silencio absoluto.
    /// 3. Artefatos de legenda/watermark: trechos contendo "legendas" ou "www." sao alucinacoes
    ///    tipicas herdadas do material de treino (creditos de legendagem, URLs) e nunca fazem
    ///    parte da oitiva real, entao sao removidos.
    /// Este overload e a primeira barreira, aplicada antes de calcular os tempos do segmento.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento ja normalizado.</param>
    /// <param name="noSpeechProb">Probabilidade [0..1] de o segmento NAO conter fala.</param>
    /// <param name="avgLogProb">Log-probabilidade media dos tokens (mais negativo = menos confianca).</param>
    /// <returns>true se o segmento deve ser descartado; caso contrario, false.</returns>
    private static bool DeveDescartarSegmento(string textoNormalizado, double noSpeechProb, double avgLogProb)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
        {
            return true;
        }

        // Ruido/alucinacao comum do Whisper em silencio absoluto.
        if (noSpeechProb >= 0.95 && avgLogProb <= -1.0)
        {
            return true;
        }

        // Artefatos classicos de legenda/watermark.
        if (textoNormalizado.Contains("legendas") || textoNormalizado.Contains("www."))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Filtro avancado de alucinacao: usa tempo, duracao e compressao alem das metricas de confianca.
    /// </summary>
    /// <remarks>
    /// COMO funciona (significado dos limiares):
    /// 1. <c>startSeconds &lt;= 2.0</c>: nunca descarta. Os primeiros ~2s costumam conter a abertura
    ///    legitima da oitiva (saudacao curta) e sao mais sensiveis a falso-positivo; preserva-se
    ///    o inicio para nao perder contexto.
    /// 2. <c>duracaoSeconds &lt;= 0.35</c> E <c>no_speech_prob &gt;= 0.95</c>: descarta. Segmento
    ///    ultracurto (&lt;=350ms) que o modelo classifica como quase-sem-fala e tipicamente um
    ///    "soluco" — estalo, respiracao ou palavra fantasma — sem valor de transcricao.
    /// 3. <c>compression_ratio &gt;= 2.8</c> E <c>avg_logprob &lt;= -0.70</c> E
    ///    <c>duracaoSeconds &lt;= 1.20</c>: descarta. Um compression_ratio alto (&gt;=2.8) revela texto
    ///    muito repetitivo/comprimivel (sintoma classico de loop de alucinacao do Whisper);
    ///    combinado a baixa confianca (avg_logprob &lt;= -0.70) e curta duracao (&lt;=1.2s), e quase
    ///    certo ser ruido gerado, nao fala real. As tres condicoes juntas evitam descartar falas
    ///    legitimas porem curtas que tenham apenas uma das caracteristicas.
    /// Este overload roda como segunda barreira, ja com os tempos do segmento disponiveis.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento ja normalizado (mantido por consistencia de assinatura).</param>
    /// <param name="noSpeechProb">Probabilidade [0..1] de o segmento NAO conter fala.</param>
    /// <param name="avgLogProb">Log-probabilidade media dos tokens (mais negativo = menos confianca).</param>
    /// <param name="compressionRatio">Razao de compressao do texto; valores altos indicam repeticao excessiva.</param>
    /// <param name="duracaoSeconds">Duracao do segmento em segundos (end - start, nunca negativa).</param>
    /// <param name="startSeconds">Instante inicial do segmento em segundos.</param>
    /// <returns>true se o segmento deve ser descartado; caso contrario, false.</returns>
    private static bool DeveDescartarSegmento(
        string textoNormalizado,
        double noSpeechProb,
        double avgLogProb,
        double compressionRatio,
        double duracaoSeconds,
        double startSeconds)
    {
        if (startSeconds <= 2.0d)
        {
            return false;
        }

        if (duracaoSeconds <= 0.35d && noSpeechProb >= 0.95d)
        {
            return true;
        }

        if (compressionRatio >= 2.8d && avgLogProb <= -0.70d && duracaoSeconds <= 1.20d)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detecta repeticao excessiva de palavras, sintoma tipico de loop de alucinacao do Whisper.
    /// </summary>
    /// <remarks>
    /// COMO funciona (significado dos limiares):
    /// 1. Tokeniza o texto por espacos, descartando vazios e aparando bordas.
    /// 2. Menos de 5 tokens: retorna false. Frases curtas nao tem amostra suficiente para
    ///    distinguir repeticao patologica de fala legitima.
    /// 3. Pobreza de vocabulario: com &gt;= 7 tokens mas no maximo 2 palavras DISTINTAS, o trecho
    ///    e quase certamente um loop (ex.: "sim sim sim sim sim sim sim") e e marcado como excessivo.
    /// 4. Repeticao contigua: varre os tokens em sequencia contando quantas vezes a mesma palavra
    ///    aparece imediatamente repetida; ao atingir 4 repeticoes seguidas, marca como excessivo.
    ///    O contador reinicia em 1 sempre que a palavra muda (1 = a propria ocorrencia atual).
    /// Retorna false se nenhum padrao for atingido.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento ja normalizado a ser avaliado.</param>
    /// <returns>true se houver repeticao excessiva (provavel alucinacao); caso contrario, false.</returns>
    private static bool TemRepeticaoExcessiva(string textoNormalizado)
    {
        var tokens = textoNormalizado
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length < 5)
        {
            return false;
        }

        var distintos = tokens.Distinct().Count();
        if (tokens.Length >= 7 && distintos <= 2)
        {
            return true;
        }

        var repeticoesSeguidas = 1;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == tokens[i - 1])
            {
                repeticoesSeguidas++;
                if (repeticoesSeguidas >= 4)
                {
                    return true;
                }
            }
            else
            {
                repeticoesSeguidas = 1;
            }
        }

        return false;
    }
}
