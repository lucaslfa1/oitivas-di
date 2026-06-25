using Microsoft.AspNetCore.SignalR;
using SinistroAPI.Hubs;
using SinistroAPI.Interfaces;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

public class TranscricaoOrquestradorService : ITranscricaoService
{
    private readonly AzureFastTranscricaoService _azureSpeechToText;
    private readonly AzureWhisperService _azureWhisper;
    private readonly MediaProcessorService _mediaProcessor;
    private readonly ILogger<TranscricaoOrquestradorService> _logger;
    private readonly IHubContext<AnalysisHub> _hubContext;

    public bool IsConfigured => _azureSpeechToText.IsConfigured || _azureWhisper.IsConfigured;

    public TranscricaoOrquestradorService(
        AzureFastTranscricaoService azureSpeechToText,
        AzureWhisperService azureWhisper,
        MediaProcessorService mediaProcessor,
        ILogger<TranscricaoOrquestradorService> logger,
        IHubContext<AnalysisHub> hubContext)
    {
        _azureSpeechToText = azureSpeechToText;
        _azureWhisper = azureWhisper;
        _mediaProcessor = mediaProcessor;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<string> TranscreverAudio(byte[] audioBytes, string mimeType, string? connectionId = null)
    {
        // Pre-processamento so quando realmente necessario.
        // Em audios longos comprimidos, preservar o original evita perda de inteligibilidade.
        var devePreProcessar = DevePreProcessarAudio(audioBytes.Length, mimeType);

        if (_mediaProcessor.IsEnabled && devePreProcessar)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 5);
                var processed = await _mediaProcessor.ProcessAudioAsync(audioBytes, mimeType);
                if (processed?.ProcessedBytes is { Length: > 0 })
                {
                    _logger.LogInformation(
                        "Audio otimizado: {OriginalKb} KB -> {ProcessedKb} KB",
                        audioBytes.Length / 1024,
                        processed.ProcessedBytes.Length / 1024);

                    audioBytes = processed.ProcessedBytes;
                    mimeType = "audio/mpeg";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Falha no pre-processamento, seguindo com audio original: {Error}", ex.Message);
            }
        }
        else if (_mediaProcessor.IsEnabled)
        {
            _logger.LogInformation(
                "Pre-processamento ignorado para preservar qualidade (Mime={MimeType}, SizeKb={SizeKb})",
                mimeType,
                audioBytes.Length / 1024);
        }

        _logger.LogInformation("Orquestrador: Iniciando transcricao Azure-only (Speech-to-Text -> Whisper)");
        await SendProgressAsync(connectionId, "", 10);

        string? ultimaFalha = null;

        // 1) Prioridade: Azure Speech-to-Text (Fast transcription + diarizacao).
        if (_azureSpeechToText.IsConfigured)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 35);
                _logger.LogInformation("Tentando Azure Speech-to-Text...");

                var resultadoStt = (await _azureSpeechToText.TranscreverAsync(audioBytes, mimeType) ?? string.Empty).Trim();
                if (TranscricaoPareceValida(resultadoStt))
                {
                    await SendProgressAsync(connectionId, "", 100);
                    return resultadoStt;
                }

                ultimaFalha = "Resultado do Azure Speech-to-Text com baixa densidade/qualidade.";
                _logger.LogWarning("Azure Speech-to-Text retornou transcricao fraca; tentando Whisper.");
            }
            catch (Exception ex)
            {
                ultimaFalha = ex.Message;
                _logger.LogWarning("Falha no Azure Speech-to-Text: {Error}", ex.Message);
            }
        }

        // 2) Fallback Azure-only: Whisper.
        if (_azureWhisper.IsConfigured)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 70);
                _logger.LogInformation("Tentando Azure Whisper...");

                var resultadoWhisper = (await _azureWhisper.TranscreverAsync(audioBytes, mimeType) ?? string.Empty).Trim();
                await SendProgressAsync(connectionId, "", 100);
                return resultadoWhisper;
            }
            catch (Exception ex)
            {
                ultimaFalha = ex.Message;
                _logger.LogError("Falha no Azure Whisper: {Error}", ex.Message);
            }
        }

        await SendProgressAsync(connectionId, "", 100);
        throw new InvalidOperationException(
            $"Transcricao indisponivel em modo Azure-only. Ultimo erro: {ultimaFalha ?? "desconhecido"}");
    }

    public Task<Dictionary<string, string>> ExtrairDadosOitiva(string transcricao)
    {
        _logger.LogInformation("Extracao de dados desativada em modo Azure-only.");
        return Task.FromResult(new Dictionary<string, string>());
    }

    private async Task SendProgressAsync(string? connectionId, string message, int percent)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        try
        {
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", message, percent);
        }
        catch
        {
            // Nao quebra o fluxo de transcricao por falha de progresso.
        }
    }

    private static bool DevePreProcessarAudio(int audioBytesLength, string mimeType)
    {
        // Audio muito grande tende a se beneficiar da reducao de tamanho.
        if (audioBytesLength >= 35 * 1024 * 1024)
        {
            return true;
        }

        var mime = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        return mime is "audio/wav"
            or "audio/x-wav"
            or "audio/wave"
            or "audio/vnd.wave"
            or "audio/pcm"
            or "audio/l16";
    }

    private static bool TranscricaoPareceValida(string transcricao)
    {
        if (string.IsNullOrWhiteSpace(transcricao))
        {
            return false;
        }

        var linhas = transcricao
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (linhas.Count <= 2 && transcricao.Length < 180)
        {
            return false;
        }

        var frases = new List<string>(linhas.Count);
        foreach (var linha in linhas)
        {
            var semMeta = Regex.Replace(linha, @"^\[\d{2}:\d{2}\]\s*[^:]+:\s*", string.Empty);
            var norm = Regex.Replace(semMeta.ToLowerInvariant(), @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(norm))
            {
                frases.Add(norm);
            }
        }

        if (frases.Count == 0)
        {
            return false;
        }

        var dominante = frases
            .GroupBy(f => f)
            .Select(g => new { Texto = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .First();

        var repeticaoDominante = (double)dominante.Count / frases.Count;
        if (frases.Count >= 8 && repeticaoDominante >= 0.70)
        {
            return false;
        }

        return true;
    }
}
