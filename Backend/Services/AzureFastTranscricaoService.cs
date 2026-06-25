using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

public class AzureFastTranscricaoService
{

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly AzureSpeechSettings _settings;
    private readonly TextCorrectionsWrapper _corrections;
    private readonly ILogger<AzureFastTranscricaoService> _logger;

    public bool IsConfigured =>
        _settings.Enabled &&
        _settings.SpeechToTextEnabled &&
        !string.IsNullOrWhiteSpace(ObterSpeechKey()) &&
        !string.IsNullOrWhiteSpace(ObterSpeechRegion());

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

    private string ConstruirFastTranscriptionUrl()
    {
        var region = ObterSpeechRegion().Trim().ToLowerInvariant();
        var apiVersion = string.IsNullOrWhiteSpace(_settings.SpeechToTextApiVersion)
            ? "2025-10-15"
            : _settings.SpeechToTextApiVersion.Trim();

        return $"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version={apiVersion}";
    }

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
