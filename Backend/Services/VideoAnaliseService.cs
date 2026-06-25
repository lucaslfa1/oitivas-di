using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de VÍDEOS (dashcam, câmeras de segurança, depoimentos).
/// Como o GPT-4o não ingere arquivo de vídeo, extrai keyframes via o Python Media Processor
/// (ffmpeg) e os envia como imagens para o Azure OpenAI (GPT-4o Vision).
/// </summary>
public class VideoAnaliseService
{
    private readonly ILogger<VideoAnaliseService> _logger;
    private readonly AzureOpenAIService _azureOpenAI;
    private readonly MediaProcessorService _mediaProcessor;
    private readonly PromptsOptions _prompts;

    /// <summary>Número de keyframes extraídos do vídeo para análise.</summary>
    private const int MaxKeyframes = 12;

    public VideoAnaliseService(
        ILogger<VideoAnaliseService> logger,
        AzureOpenAIService azureOpenAI,
        IOptions<PromptsOptions> promptsOptions,
        MediaProcessorService mediaProcessor)
    {
        _logger = logger;
        _azureOpenAI = azureOpenAI;
        _mediaProcessor = mediaProcessor;
        _prompts = promptsOptions.Value;
    }

    public bool IsConfigured => _azureOpenAI.IsConfigured;

    /// <summary>
    /// Analisa vídeo: extrai keyframes (Python/ffmpeg) e os envia ao GPT-4o Vision.
    /// </summary>
    public async Task<string> AnalisarVideoStream(Stream videoStream, string mimeType, string fileName, string contextoUsuario, string duracao = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de vídeo não está configurado (Azure OpenAI).");
        }

        using var ms = new MemoryStream();
        if (videoStream.CanSeek) videoStream.Position = 0;
        await videoStream.CopyToAsync(ms);
        var videoBytes = ms.ToArray();

        _logger.LogInformation("Extraindo keyframes do vídeo '{File}' ({Size} MB)...", fileName, videoBytes.Length / 1024.0 / 1024.0);

        var keyframes = await ExtractKeyframesAsync(videoBytes, mimeType, MaxKeyframes);

        if (keyframes == null || keyframes.Count == 0)
        {
            throw new InvalidOperationException(
                "Não foi possível extrair quadros do vídeo. Verifique se o serviço de processamento de vídeo (Python Media Processor) está disponível e habilitado.");
        }

        _logger.LogInformation("Analisando {Count} keyframes via Azure OpenAI (GPT-4o Vision)...", keyframes.Count);

        var imagens = keyframes.Select(k => (Base64: k, MimeType: "image/jpeg")).ToList();
        var prompt = $"{GetVideoPrompt(duracao)}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}";

        return await _azureOpenAI.GenerateVisionAsync(prompt, "", imagens);
    }

    /// <summary>
    /// Monta o prompt completo de análise de vídeo usando configuração externa
    /// </summary>
    private string GetVideoPrompt(string duracao)
    {
        var duracaoInfo = !string.IsNullOrWhiteSpace(duracao) ? duracao : "Não informada";
        var template = _prompts.Video.Template.Replace("{duracao}", duracaoInfo);
        return $"{_prompts.MasterForensicContext}\n\n{template}";
    }

    /// <summary>
    /// Extrai keyframes do vídeo via Python Media Processor.
    /// </summary>
    /// <param name="videoBytes">Bytes do vídeo</param>
    /// <param name="mimeType">Tipo MIME do vídeo</param>
    /// <param name="maxKeyframes">Número máximo de keyframes</param>
    /// <returns>Lista de imagens em base64 (JPEG) ou null se falhar/indisponível</returns>
    public async Task<List<string>?> ExtractKeyframesAsync(byte[] videoBytes, string mimeType, int maxKeyframes = 10)
    {
        if (!_mediaProcessor.IsEnabled)
        {
            _logger.LogWarning("Python Media Processor desabilitado, extração de keyframes não disponível");
            return null;
        }

        try
        {
            var result = await _mediaProcessor.ProcessVideoAsync(videoBytes, mimeType, maxKeyframes);

            if (result != null && result.KeyframesCount > 0)
            {
                _logger.LogInformation("Extraídos {Count} keyframes com sucesso", result.KeyframesCount);
                return result.KeyframesBase64;
            }

            _logger.LogWarning("Nenhum keyframe extraído");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Falha ao extrair keyframes: {Error}", ex.Message);
            return null;
        }
    }
}
