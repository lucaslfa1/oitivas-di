using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;

namespace SinistroAPI.Services.AI;

/// <summary>
/// Implementação do provedor Gemini (Google AI Studio)
/// Suporta inline_data e File API para arquivos grandes
/// </summary>
public class GeminiProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly GeminiFileApiService _fileApiService;
    private readonly string _defaultModel;
    
    private const string API_BASE_URL = "https://generativelanguage.googleapis.com/v1beta";
    private const int INLINE_DATA_LIMIT = 15 * 1024 * 1024; // 15MB

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public string ProviderName => "Gemini";

    public GeminiProvider(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<GeminiProvider> logger,
        GeminiFileApiService fileApiService,
        IOptions<VertexAISettings> vertexSettings)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(15);
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _logger = logger;
        _fileApiService = fileApiService;
        _defaultModel = vertexSettings.Value.Models.Transcription ?? "gemini-1.5-pro";
    }

    public async Task<string> GenerateTextAsync(string prompt, string? systemPrompt = null, float temperature = 0.0f)
    {
        if (!IsConfigured) throw new InvalidOperationException("Gemini API não configurada.");

        var parts = new List<object> { new { text = prompt } };
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            parts.Insert(0, new { text = systemPrompt });
        }

        var payload = new
        {
            contents = new object[]
            {
                new { role = "user", parts = parts.ToArray() }
            },
            generationConfig = new
            {
                temperature = temperature,
                maxOutputTokens = 8192
            }
        };

        return await SendRequestAsync(payload);
    }

    public async Task<string> AnalyzeMediaAsync(string prompt, byte[] mediaBytes, string mimeType, string? systemPrompt = null, float temperature = 0.3f)
    {
        if (!IsConfigured) throw new InvalidOperationException("Gemini API não configurada.");

        // Para arquivos grandes, usar File API
        if (mediaBytes.Length > INLINE_DATA_LIMIT)
        {
            return await AnalyzeLargeMediaAsync(prompt, mediaBytes, mimeType, $"media_{DateTime.Now:yyyyMMdd_HHmmss}", systemPrompt, temperature);
        }

        var base64Data = Convert.ToBase64String(mediaBytes);
        
        var parts = new List<object>();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            parts.Add(new { text = systemPrompt });
        }
        parts.Add(new { text = prompt });
        parts.Add(new { inline_data = new { mime_type = mimeType, data = base64Data } });

        var payload = new
        {
            contents = new object[]
            {
                new { role = "user", parts = parts.ToArray() }
            },
            generationConfig = new
            {
                temperature = temperature,
                maxOutputTokens = 16384
            }
        };

        return await SendRequestAsync(payload);
    }

    public async Task<string> AnalyzeLargeMediaAsync(string prompt, byte[] mediaBytes, string mimeType, string displayName, string? systemPrompt = null, float temperature = 0.3f)
    {
        if (!IsConfigured) throw new InvalidOperationException("Gemini API não configurada.");

        string? fileUri = null;
        string? fileName = null;

        try
        {
            _logger.LogInformation("Upload de arquivo grande ({Size} MB) via File API", mediaBytes.Length / 1024.0 / 1024.0);
            
            // 1. Upload do arquivo
            fileUri = await _fileApiService.UploadFileAsync(mediaBytes, mimeType, displayName);
            
            // Extrair nome para deletar depois
            var uriParts = fileUri.Split('/');
            fileName = $"files/{uriParts[^1]}";

            // 2. Enviar requisição com file_data
            var parts = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                parts.Add(new { text = systemPrompt });
            }
            parts.Add(new { text = prompt });
            parts.Add(new { file_data = new { mime_type = mimeType, file_uri = fileUri } });

            var payload = new
            {
                contents = new object[]
                {
                    new { role = "user", parts = parts.ToArray() }
                },
                generationConfig = new
                {
                    temperature = temperature,
                    maxOutputTokens = 32768
                }
            };

            return await SendRequestAsync(payload);
        }
        finally
        {
            // 3. Limpar arquivo
            if (!string.IsNullOrEmpty(fileName))
            {
                try
                {
                    await _fileApiService.DeleteFileAsync(fileName);
                    _logger.LogInformation("Arquivo temporário deletado: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Falha ao deletar arquivo temporário: {Error}", ex.Message);
                }
            }
        }
    }

    private async Task<string> SendRequestAsync(object payload)
    {
        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var url = $"{API_BASE_URL}/models/{_defaultModel}:generateContent?key={_apiKey}";

        _logger.LogInformation("Enviando requisição para Gemini ({Model})", _defaultModel);

        var response = await _httpClient.PostAsync(url, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Erro Gemini: {StatusCode} - {Error}", response.StatusCode, error);
            throw new Exception($"Erro Gemini ({response.StatusCode}): {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        try
        {
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "Sem resposta.";
        }
        catch
        {
            _logger.LogError("Erro ao parsear resposta do Gemini");
            return "Erro ao processar resposta.";
        }
    }
}
