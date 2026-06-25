using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço para integração com Azure OpenAI (GPT-4o)
/// </summary>
public class AzureOpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<AzureOpenAIService> _logger;

    public bool IsConfigured => _settings.Enabled && !string.IsNullOrEmpty(_settings.ApiKey);

    public AzureOpenAIService(
        HttpClient httpClient,
        IOptions<AzureOpenAISettings> settings,
        ILogger<AzureOpenAIService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gera conteúdo usando GPT-4o (Texto ou Imagem)
    /// </summary>
    public async Task<string> GenerateContentAsync(string prompt, string systemPrompt = "", string? base64Image = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure OpenAI não está configurado.");

        var url = $"{_settings.Endpoint}openai/deployments/{_settings.DeploymentName}/chat/completions?api-version=2024-02-15-preview";

        var messages = new List<object>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        if (!string.IsNullOrEmpty(base64Image))
        {
            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                }
            });
        }
        else
        {
            messages.Add(new { role = "user", content = prompt });
        }

        var payload = new
        {
            messages = messages,
            max_tokens = 4096,
            temperature = 0.0, // Alta precisão para laudos periciais
            top_p = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        _logger.LogInformation("Enviando requisição para Azure OpenAI: {Deployment}", _settings.DeploymentName);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Erro Azure OpenAI ({Status}): {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Azure OpenAI error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString() ?? "Sem resposta.";
    }
}
