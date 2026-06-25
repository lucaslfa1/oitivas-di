using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

public class AzureWhisperService
{

    private readonly HttpClient _httpClient;
    private readonly AzureSpeechSettings _settings;
    private readonly TextCorrectionsWrapper _corrections;
    private readonly ILogger<AzureWhisperService> _logger;

    public bool IsConfigured =>
        _settings.Enabled &&
        !string.IsNullOrWhiteSpace(_settings.SubscriptionKey) &&
        !string.IsNullOrWhiteSpace(_settings.Endpoint);

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

    private static string NormalizarTexto(string texto)
    {
        return SpeakerDetectionService.NormalizarTexto(texto);
    }

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
