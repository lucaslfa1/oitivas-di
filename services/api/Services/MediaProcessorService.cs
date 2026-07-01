using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço para comunicação com o Python Media Processor.
/// Fornece normalização de áudio, redução de ruído, extração de keyframes e análise de sentimento.
/// </summary>
public class MediaProcessorService : IMediaProcessorService
{
    private readonly HttpClient _httpClient;
    private readonly MediaProcessorSettings _settings;
    private readonly ILogger<MediaProcessorService> _logger;

    public MediaProcessorService(
        HttpClient httpClient,
        IOptions<MediaProcessorSettings> settings,
        ILogger<MediaProcessorService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    /// <summary>
    /// Verifica se o Python processor está habilitado nas configurações
    /// </summary>
    public bool IsEnabled => _settings.Enabled;

    /// <summary>
    /// Verifica se o serviço Python está disponível (health check)
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        if (!_settings.Enabled) return false;

        try
        {
            var response = await _httpClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Python Media Processor não disponível: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Pré-processa áudio: normalização de volume e redução de ruído.
    /// Retorna null se o processamento falhar (usar áudio original como fallback).
    /// </summary>
    public async Task<ProcessedAudioResult?> ProcessAudioAsync(byte[] audioBytes, string mimeType)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Media Processor desabilitado, usando áudio original");
            return null;
        }

        try
        {
            _logger.LogInformation("Enviando áudio para pré-processamento ({Size} KB)...", audioBytes.Length / 1024);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audioBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            
            // Determinar extensão baseado no mime type
            var extension = GetExtensionFromMimeType(mimeType);
            content.Add(fileContent, "file", $"audio{extension}");

            var response = await _httpClient.PostAsync("/process/audio", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha no pré-processamento de áudio: {Error}", error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            
            // LOG DE DEBUG: Ver o que o Python retornou
            if (json.Length > 500) 
                _logger.LogInformation("Resposta do Python (primeiros 500 chars): {Json}...", json.Substring(0, 500));
            else
                _logger.LogInformation("Resposta do Python: {Json}", json);

            var result = JsonSerializer.Deserialize<ProcessedAudioResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (result == null || !result.Success || string.IsNullOrEmpty(result.ProcessedFileBase64))
            {
                _logger.LogWarning("Resposta inválida do Media Processor");
                return null;
            }

            var processedBytes = Convert.FromBase64String(result.ProcessedFileBase64);
            
            _logger.LogInformation(
                "Áudio pré-processado: {Original} KB -> {Processed} KB", 
                result.OriginalSizeBytes / 1024, 
                result.ProcessedSizeBytes / 1024);

            return new ProcessedAudioResult
            {
                ProcessedBytes = processedBytes,
                OriginalSizeBytes = result.OriginalSizeBytes,
                ProcessedSizeBytes = result.ProcessedSizeBytes,
                Metadata = result.Metadata
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout ao pré-processar áudio");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao pré-processar áudio: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Converte áudio para WAV usando endpoint de streaming (mais eficiente para arquivos grandes).
    /// </summary>
    public async Task<ProcessedAudioResult?> ConvertAudioToWavAsync(byte[] audioBytes, string mimeType)
    {
        if (!_settings.Enabled) return null;

        try
        {
            _logger.LogInformation("Convertendo áudio para WAV (Streaming) - {Size} KB...", audioBytes.Length / 1024);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audioBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            
            var extension = GetExtensionFromMimeType(mimeType);
            content.Add(fileContent, "file", $"audio{extension}");

            // Chama o endpoint de ferramentas que retorna stream binário
            var response = await _httpClient.PostAsync("/tools/convert-to-wav", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha na conversão WAV: {Error}", error);
                return null;
            }

            // Lê o stream diretamente como array de bytes
            var processedBytes = await response.Content.ReadAsByteArrayAsync();
            
            _logger.LogInformation(
                "Áudio convertido para WAV: {Original} KB -> {Processed} KB", 
                audioBytes.Length / 1024, 
                processedBytes.Length / 1024);

            return new ProcessedAudioResult
            {
                ProcessedBytes = processedBytes,
                OriginalSizeBytes = audioBytes.Length,
                ProcessedSizeBytes = processedBytes.Length,
                Metadata = new Dictionary<string, object> { { "format", "wav" }, { "method", "streaming" } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao converter áudio (WAV): {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extrai keyframes de vídeo para análise.
    /// Retorna null se o processamento falhar.
    /// </summary>
    public async Task<ProcessedVideoResult?> ProcessVideoAsync(byte[] videoBytes, string mimeType, int maxKeyframes = 10)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Media Processor desabilitado");
            return null;
        }

        try
        {
            _logger.LogInformation("Enviando vídeo para extração de keyframes ({Size} MB)...", videoBytes.Length / 1024.0 / 1024.0);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(videoBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            
            var extension = GetExtensionFromMimeType(mimeType);
            content.Add(fileContent, "file", $"video{extension}");
            content.Add(new StringContent("true"), "extract_keyframes");
            content.Add(new StringContent(maxKeyframes.ToString()), "max_keyframes");

            var response = await _httpClient.PostAsync("/process/video", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha na extração de keyframes: {Error}", error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProcessedVideoResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (result == null || !result.Success)
            {
                _logger.LogWarning("Resposta inválida do Media Processor (video)");
                return null;
            }

            _logger.LogInformation("Vídeo processado: {Keyframes} keyframes extraídos", result.KeyframesCount);

            return new ProcessedVideoResult
            {
                KeyframesBase64 = result.KeyframesBase64 ?? new List<string>(),
                KeyframesCount = result.KeyframesCount,
                OriginalSizeBytes = result.OriginalSizeBytes
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout ao processar vídeo");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao processar vídeo: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Analisa o sentimento/tom de voz do áudio.
    /// </summary>
    public async Task<SentimentResult?> AnalyzeSentimentAsync(byte[] audioBytes, string mimeType)
    {
        if (!_settings.Enabled) return null;

        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audioBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            
            var extension = GetExtensionFromMimeType(mimeType);
            content.Add(fileContent, "file", $"audio{extension}");

            var response = await _httpClient.PostAsync("/analyze/sentiment", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Falha na análise de sentimento: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SentimentResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (result == null || !result.Success) return null;

            return new SentimentResult
            {
                Classification = result.Classification,
                Description = result.Description,
                Metrics = result.Metrics
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao analisar sentimento: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extrai áudio de um arquivo de vídeo.
    /// </summary>
    public async Task<ExtractedAudioResult?> ExtractAudioFromVideoAsync(byte[] videoBytes, string mimeType)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Media Processor desabilitado");
            return null;
        }

        try
        {
            _logger.LogInformation("Extraindo áudio de vídeo ({Size} MB)...", videoBytes.Length / 1024.0 / 1024.0);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(videoBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            
            var extension = GetExtensionFromMimeType(mimeType);
            content.Add(fileContent, "file", $"video{extension}");

            var response = await _httpClient.PostAsync("/extract/audio", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha na extração de áudio do vídeo: {Error}", error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ExtractedAudioResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (result == null || !result.Success || string.IsNullOrEmpty(result.AudioFileBase64))
            {
                _logger.LogWarning("Resposta inválida do Media Processor (extração áudio)");
                return null;
            }

            var audioBytes = Convert.FromBase64String(result.AudioFileBase64);
            
            _logger.LogInformation(
                "Áudio extraído de vídeo: {VideoSize} KB -> {AudioSize} KB", 
                result.OriginalVideoSizeBytes / 1024, 
                result.ExtractedAudioSizeBytes / 1024);

            return new ExtractedAudioResult
            {
                AudioBytes = audioBytes,
                OriginalVideoSizeBytes = result.OriginalVideoSizeBytes,
                ExtractedAudioSizeBytes = result.ExtractedAudioSizeBytes,
                AudioFormat = result.AudioFormat ?? "mp3",
                VideoDurationSeconds = result.VideoDurationSeconds
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout ao extrair áudio do vídeo");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao extrair áudio do vídeo: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Envia múltiplos áudios para serem mesclados em um único arquivo.
    /// O endpoint Python retorna binary stream (não base64 JSON) para evitar limites de tamanho.
    /// </summary>
    public async Task<ProcessedAudioResult?> MergeAudiosAsync(List<byte[]> audioFiles, string mimeType)
    {
        if (!_settings.Enabled || audioFiles == null || audioFiles.Count == 0) return null;

        try
        {
            _logger.LogInformation("Enviando {Count} áudios para merge...", audioFiles.Count);

            using var content = new MultipartFormDataContent();
            var extension = GetExtensionFromMimeType(mimeType);

            for (int i = 0; i < audioFiles.Count; i++)
            {
                var fileContent = new ByteArrayContent(audioFiles[i]);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
                // "files" deve corresponder ao nome do parâmetro no FastAPI (files: list[UploadFile])
                content.Add(fileContent, "files", $"input_{i}{extension}");
            }

            var response = await _httpClient.PostAsync("/process/merge-audio", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha no merge de áudio: {StatusCode} - {Error}", response.StatusCode, error);
                return null;
            }

            // Resposta agora é binary stream direto (não JSON com base64)
            var processedBytes = await response.Content.ReadAsByteArrayAsync();

            if (processedBytes == null || processedBytes.Length == 0)
            {
                _logger.LogWarning("Resposta vazia do Media Processor (merge)");
                return null;
            }

            // Ler metadata dos headers (opcional)
            var originalSize = 0;
            var processedSize = processedBytes.Length;
            if (response.Headers.TryGetValues("X-Original-Size", out var origValues))
                int.TryParse(origValues.FirstOrDefault(), out originalSize);

            _logger.LogInformation(
                "Merge concluído: {Original} KB (Total) -> {Processed} KB", 
                originalSize / 1024, 
                processedSize / 1024);

            return new ProcessedAudioResult
            {
                ProcessedBytes = processedBytes,
                OriginalSizeBytes = originalSize,
                ProcessedSizeBytes = processedSize,
                Metadata = new Dictionary<string, object> { { "format", "mp3" }, { "method", "binary_stream" } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao fazer merge de áudios: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Envia uma imagem base64 e as marcações para serem desenhadas no Python Media Processor.
    /// Retorna a imagem base64 anotada ou null se falhar.
    /// </summary>
    public async Task<string?> AnnotateImageAsync(string imageBase64, List<ImageAnnotation> annotations)
    {
        if (!_settings.Enabled) return null;

        try
        {
            _logger.LogInformation("Enviando imagem para anotação no Media Processor ({Count} marcações)...", annotations.Count);

            var request = new AnnotateImageRequestDto
            {
                ImageBase64 = imageBase64,
                Annotations = annotations.Select(a => new AnnotationItemDto
                {
                    Type = a.Type,
                    Coordinates = a.Coordinates,
                    Label = a.Label
                }).ToList()
            };

            var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/process/annotate", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha ao anotar imagem: {Error}", error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AnnotateImageResponseDto>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null || !result.Success || string.IsNullOrEmpty(result.AnnotatedImageBase64))
            {
                _logger.LogWarning("Resposta inválida do Media Processor ao anotar imagem");
                return null;
            }

            return result.AnnotatedImageBase64;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao anotar imagem: {Error}", ex.Message);
            return null;
        }
    }

    private static string GetExtensionFromMimeType(string mimeType) => mimeType.ToLower() switch
    {
        "audio/mpeg" => ".mp3",
        "audio/mp3" => ".mp3",
        "audio/wav" => ".wav",
        "audio/x-wav" => ".wav",
        "audio/ogg" => ".ogg",
        "audio/m4a" => ".m4a",
        "audio/x-m4a" => ".m4a",
        "audio/mp4" => ".m4a",
        "video/mp4" => ".mp4",
        "video/quicktime" => ".mov",
        "video/x-msvideo" => ".avi",
        "video/webm" => ".webm",
        "video/mpeg" => ".mpeg",
        "video/mpg" => ".mpeg",
        _ => ".bin"
    };

    // --- Response DTOs (Internos) ---

    private class ProcessedAudioResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("original_size_bytes")]
        public int OriginalSizeBytes { get; set; }

        [JsonPropertyName("processed_size_bytes")]
        public int ProcessedSizeBytes { get; set; }

        [JsonPropertyName("processed_file_base64")]
        public string? ProcessedFileBase64 { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class ProcessedVideoResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("original_size_bytes")]
        public int OriginalSizeBytes { get; set; }

        [JsonPropertyName("keyframes_base64")]
        public List<string>? KeyframesBase64 { get; set; }

        [JsonPropertyName("keyframes_count")]
        public int KeyframesCount { get; set; }
    }

    private class SentimentResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("classification")]
        public string Classification { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("metrics")]
        public Dictionary<string, double>? Metrics { get; set; }
    }

    private class ExtractedAudioResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("original_video_size_bytes")]
        public int OriginalVideoSizeBytes { get; set; }

        [JsonPropertyName("extracted_audio_size_bytes")]
        public int ExtractedAudioSizeBytes { get; set; }

        [JsonPropertyName("audio_file_base64")]
        public string? AudioFileBase64 { get; set; }

        [JsonPropertyName("audio_format")]
        public string? AudioFormat { get; set; }

        [JsonPropertyName("audio_bitrate")]
        public string? AudioBitrate { get; set; }

        [JsonPropertyName("video_duration_seconds")]
        public double VideoDurationSeconds { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private class AnnotationItemDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("coordinates")]
        public List<int> Coordinates { get; set; } = new();

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    private class AnnotateImageRequestDto
    {
        [JsonPropertyName("image_base64")]
        public string ImageBase64 { get; set; } = "";

        [JsonPropertyName("annotations")]
        public List<AnnotationItemDto> Annotations { get; set; } = new();
    }

    private class AnnotateImageResponseDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("annotated_image_base64")]
        public string? AnnotatedImageBase64 { get; set; }
    }
}

// --- Public DTOs ---

/// <summary>
/// Resultado do pré-processamento de áudio
/// </summary>
public class ProcessedAudioResult
{
    public required byte[] ProcessedBytes { get; set; }
    public int OriginalSizeBytes { get; set; }
    public int ProcessedSizeBytes { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Resultado do processamento de vídeo
/// </summary>
public class ProcessedVideoResult
{
    public required List<string> KeyframesBase64 { get; set; }
    public int KeyframesCount { get; set; }
    public int OriginalSizeBytes { get; set; }
}

/// <summary>
/// Resultado da análise de sentimento
/// </summary>
public class SentimentResult
{
    public string Classification { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, double>? Metrics { get; set; }
}

/// <summary>
/// Resultado da extração de áudio de vídeo
/// </summary>
public class ExtractedAudioResult
{
    public required byte[] AudioBytes { get; set; }
    public int OriginalVideoSizeBytes { get; set; }
    public int ExtractedAudioSizeBytes { get; set; }
    public string AudioFormat { get; set; } = "mp3";
    public double VideoDurationSeconds { get; set; }
}

/// <summary>
/// Marcação visual para desenhar na imagem
/// </summary>
public class ImageAnnotation
{
    public string Type { get; set; } = "";
    public List<int> Coordinates { get; set; } = new();
    public string? Label { get; set; }
}
