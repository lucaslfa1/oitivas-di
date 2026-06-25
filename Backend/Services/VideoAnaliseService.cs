using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de VÍDEOS (dashcam, câmeras de segurança, depoimentos)
/// Utiliza Gemini File API para upload de vídeos grandes (até 2GB)
/// Integração com Python Media Processor para extração de keyframes
/// </summary>
public class VideoAnaliseService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<VideoAnaliseService> _logger;
    private readonly VertexAIService _vertexAI;
    private readonly VertexAISettings _vertexSettings;
    private readonly MediaProcessorService _mediaProcessor;
    private readonly string _videoAnalysisModel;
    private readonly PromptsOptions _prompts;
    private readonly UploadLimitsOptions _uploadLimits;
    private const string API_URL = "https://generativelanguage.googleapis.com/v1beta";

    public VideoAnaliseService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<VideoAnaliseService> logger, 
        VertexAIService vertexAI, 
        IOptions<VertexAISettings> vertexSettings,
        IOptions<PromptsOptions> promptsOptions,
        IOptions<UploadLimitsOptions> uploadLimits,
        MediaProcessorService mediaProcessor)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _logger = logger;
        _vertexAI = vertexAI;
        _vertexSettings = vertexSettings.Value;
        _mediaProcessor = mediaProcessor;
        _videoAnalysisModel = _vertexSettings.Models.VideoAnalysis ?? "gemini-1.5-pro";
        _prompts = promptsOptions.Value;
        _uploadLimits = uploadLimits.Value;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) || _vertexAI.IsConfigured;

    /// <summary>
    /// Upload de vídeo usando o protocolo resumable da Gemini File API
    /// </summary>
    private async Task<string> UploadVideoAsync(Stream videoStream, string mimeType, string fileName, long fileSize)
    {
        _logger.LogInformation("Iniciando upload resumable de vídeo ({MimeType}, {Size}KB)", mimeType, fileSize / 1024);
        
        // Passo 1: Iniciar sessão de upload resumable
        var initUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={_apiKey}";
        
        var initRequest = new HttpRequestMessage(HttpMethod.Post, initUrl);
        initRequest.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        initRequest.Headers.Add("X-Goog-Upload-Command", "start");
        initRequest.Headers.Add("X-Goog-Upload-Header-Content-Length", fileSize.ToString());
        initRequest.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
        
        var metadata = new { file = new { display_name = fileName } };
        initRequest.Content = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
        
        var initResponse = await _httpClient.SendAsync(initRequest);
        
        if (!initResponse.IsSuccessStatusCode)
        {
            var erro = await initResponse.Content.ReadAsStringAsync();
            _logger.LogError("Erro ao iniciar upload: {StatusCode} - {Erro}", initResponse.StatusCode, erro);
            throw new Exception($"Falha ao iniciar upload: {erro}");
        }
        
        // Obter URL de upload do header
        if (!initResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var uploadUrls))
        {
            throw new Exception("Não foi possível obter URL de upload.");
        }
        var uploadUrl = uploadUrls.First();
        
        _logger.LogInformation("Sessão de upload iniciada, enviando dados...");
        
        // Passo 2: Enviar o arquivo completo
        var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        uploadRequest.Headers.Add("X-Goog-Upload-Offset", "0");
        
        // Ler o stream em chunks para evitar carregar tudo na memória
        if (videoStream.CanSeek)
        {
            videoStream.Position = 0;
        }

        uploadRequest.Content = new StreamContent(videoStream);
        uploadRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = fileSize;
        
        var uploadResponse = await _httpClient.SendAsync(uploadRequest);
        
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var erro = await uploadResponse.Content.ReadAsStringAsync();
            _logger.LogError("Erro no upload: {StatusCode} - {Erro}", uploadResponse.StatusCode, erro);
            throw new Exception($"Falha no upload do vídeo: {erro}");
        }
        
        var responseJson = await uploadResponse.Content.ReadAsStringAsync();
        _logger.LogInformation("Upload concluído: {Response}", responseJson);
        
        using var doc = JsonDocument.Parse(responseJson);
        
        var fileUri = doc.RootElement.GetProperty("file").GetProperty("uri").GetString();
        var state = doc.RootElement.GetProperty("file").GetProperty("state").GetString();
        
        _logger.LogInformation("Upload concluído. URI: {Uri}, Estado: {State}", fileUri, state);
        
        // Aguarda processamento se necessário
        if (state == "PROCESSING")
        {
            fileUri = await WaitForFileProcessingAsync(fileUri!);
        }
        
        return fileUri ?? throw new Exception("Não foi possível obter o URI do arquivo.");
    }

    /// <summary>
    /// Aguarda o processamento do vídeo
    /// </summary>
    private async Task<string> WaitForFileProcessingAsync(string fileUri)
    {
        var fileName = fileUri.Split('/').Last();
        var statusUrl = $"{API_URL}/files/{fileName}?key={_apiKey}";
        
        for (int i = 0; i < 60; i++) // Máximo 5 minutos de espera
        {
            await Task.Delay(5000);
            
            var response = await _httpClient.GetAsync(statusUrl);
            if (!response.IsSuccessStatusCode) continue;
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var state = doc.RootElement.GetProperty("state").GetString();
            _logger.LogInformation("Estado do arquivo: {State}", state);
            
            if (state == "ACTIVE")
            {
                return doc.RootElement.GetProperty("uri").GetString()!;
            }
            else if (state == "FAILED")
            {
                throw new Exception("Processamento do vídeo falhou no Gemini.");
            }
        }
        
        throw new TimeoutException("Timeout aguardando processamento do vídeo.");
    }

    /// <summary>
    /// Analisa vídeo usando stream (para vídeos grandes)
    /// </summary>
    public async Task<string> AnalisarVideoStream(Stream videoStream, string mimeType, string fileName, string contextoUsuario, string duracao = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de vídeo não está configurado.");
        }

        long fileSize = videoStream.Length;

        if (_vertexAI.IsConfigured && fileSize <= _uploadLimits.MaxVertexInlineBytes)
        {
            _logger.LogInformation("Analisando vídeo via Vertex AI...");
            
            // Convert stream to base64 for Vertex AI (Note: Vertex AI has limits on base64 size, usually 20MB. For larger files, we should use GCS)
            // Assuming for now we are dealing with files that fit in memory/base64 limits or user accepts this limitation.
            // Ideally, we would upload to GCS, but that requires a bucket.
            // For now, let's try reading to memory. If it's too big, we might need a different approach (GCS).
            
            using var ms = new MemoryStream();
            await videoStream.CopyToAsync(ms);
            var videoBytes = ms.ToArray();
            var base64Video = Convert.ToBase64String(videoBytes);
            
            var prompt = GetVideoPrompt(duracao);
            return await _vertexAI.GenerateContentAsync(_vertexSettings.Models.VideoAnalysis, $"{prompt}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}", null, base64Video, mimeType);
        }
        if (_vertexAI.IsConfigured && fileSize > _uploadLimits.MaxVertexInlineBytes && string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Video too large for Vertex inline upload. Configure GEMINI_API_KEY or use a smaller file.");
        }


        
        // 1. Upload do vídeo para Gemini File API
        var fileUri = await UploadVideoAsync(videoStream, mimeType, fileName, fileSize);
        
        // 2. Analisa usando o file URI
        return await AnalisarComFileUri(fileUri, mimeType, contextoUsuario, duracao);
    }

    /// <summary>
    /// Analisa vídeo usando file URI (após upload)
    /// </summary>
    private async Task<string> AnalisarComFileUri(string fileUri, string mimeType, string contextoUsuario, string duracao)
    {
        _logger.LogInformation("Analisando vídeo via File URI: {Uri}", fileUri);
        
        var prompt = GetVideoPrompt(duracao);
        
        var payload = new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"{prompt}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}" },
                        new {
                            file_data = new {
                                mime_type = mimeType,
                                file_uri = fileUri
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 8192
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var url = $"{API_URL}/models/{_videoAnalysisModel}:generateContent?key={_apiKey}";
        
        var resposta = await _httpClient.PostAsync(url, jsonContent);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync();
            _logger.LogError("Erro Gemini: {StatusCode} - {Erro}", resposta.StatusCode, erro);
            throw new Exception($"Erro na análise ({resposta.StatusCode}): {erro}");
        }

        var respostaString = await resposta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respostaString);
        
        try 
        {
            return doc.RootElement.GetProperty("candidates")[0]
                                  .GetProperty("content")
                                  .GetProperty("parts")[0]
                                  .GetProperty("text")
                                  .GetString() ?? "Sem resposta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar resposta do Gemini");
            return "Erro ao processar o laudo.";
        }
    }

    /// <summary>
    /// Método legado para vídeos pequenos (usa base64 inline)
    /// </summary>
    public async Task<string> AnalisarVideo(string base64Video, string mimeType, string contextoUsuario, string duracao = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de vídeo não está configurado.");
        }

        _logger.LogInformation("Analisando vídeo via base64 inline ({MimeType})", mimeType);

        var prompt = GetVideoPrompt(duracao);

        if (_vertexAI.IsConfigured)
        {
            return await _vertexAI.GenerateContentAsync(_vertexSettings.Models.VideoAnalysis, $"{prompt}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}", "", base64Video, mimeType);
        }

        var payload = new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"{prompt}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}" },
                        new {
                            inline_data = new {
                                mime_type = mimeType,
                                data = base64Video
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 8192
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var url = $"{API_URL}/models/{_videoAnalysisModel}:generateContent?key={_apiKey}";
        
        var resposta = await _httpClient.PostAsync(url, jsonContent);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync();
            _logger.LogError("Erro Gemini: {StatusCode} - {Erro}", resposta.StatusCode, erro);
            throw new Exception($"Erro na análise ({resposta.StatusCode}): {erro}");
        }

        var respostaString = await resposta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respostaString);
        
        try 
        {
            return doc.RootElement.GetProperty("candidates")[0]
                                  .GetProperty("content")
                                  .GetProperty("parts")[0]
                                  .GetProperty("text")
                                  .GetString() ?? "Sem resposta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar resposta do Gemini");
            return "Erro ao processar o laudo.";
        }
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
    /// Útil para análise rápida de frames importantes sem processar o vídeo inteiro.
    /// </summary>
    /// <param name="videoBytes">Bytes do vídeo</param>
    /// <param name="mimeType">Tipo MIME do vídeo</param>
    /// <param name="maxKeyframes">Número máximo de keyframes (default: 10)</param>
    /// <returns>Lista de imagens em base64 (JPEG) ou null se falhar</returns>
    public async Task<List<string>?> ExtractKeyframesAsync(byte[] videoBytes, string mimeType, int maxKeyframes = 10)
    {
        if (!_mediaProcessor.IsEnabled)
        {
            _logger.LogDebug("Python Media Processor desabilitado, extração de keyframes não disponível");
            return null;
        }

        try
        {
            _logger.LogInformation("Extraindo keyframes via Python ({Size} MB)...", videoBytes.Length / 1024.0 / 1024.0);
            
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
