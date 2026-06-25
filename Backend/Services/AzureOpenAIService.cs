using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço para integração com Azure OpenAI (GPT-4o).
/// Suporta texto, imagem única e múltiplas imagens (keyframes de vídeo).
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
    /// Gera conteúdo usando GPT-4o (texto ou imagem única).
    /// </summary>
    public async Task<string> GenerateContentAsync(string prompt, string systemPrompt = "", string? base64Image = null, string mimeType = "image/jpeg")
    {
        var images = string.IsNullOrEmpty(base64Image)
            ? new List<(string Base64, string MimeType)>()
            : new List<(string Base64, string MimeType)> { (base64Image, mimeType) };

        return await GenerateVisionAsync(prompt, systemPrompt, images);
    }

    /// <summary>
    /// Gera conteúdo usando GPT-4o com múltiplas imagens (ex.: keyframes extraídos de vídeo).
    /// Se <paramref name="captions"/> for fornecido, cada legenda é inserida como texto antes
    /// da imagem correspondente (ex.: o timestamp do quadro), para ancorar a análise no tempo.
    /// </summary>
    public async Task<string> GenerateVisionAsync(
        string prompt,
        string systemPrompt,
        List<(string Base64, string MimeType)> images,
        int maxTokens = 8192,
        List<string>? captions = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure OpenAI não está configurado.");

        var url = $"{_settings.Endpoint}openai/deployments/{_settings.DeploymentName}/chat/completions?api-version=2024-02-15-preview";

        var messages = new List<object>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        if (images.Count > 0)
        {
            var content = new List<object> { new { type = "text", text = prompt } };
            for (int i = 0; i < images.Count; i++)
            {
                if (captions != null && i < captions.Count && !string.IsNullOrEmpty(captions[i]))
                {
                    content.Add(new { type = "text", text = captions[i] });
                }

                var (base64, mime) = images[i];
                var mimeReal = string.IsNullOrEmpty(mime) ? "image/jpeg" : mime;
                content.Add(new { type = "image_url", image_url = new { url = $"data:{mimeReal};base64,{base64}" } });
            }
            messages.Add(new { role = "user", content = content.ToArray() });
        }
        else
        {
            messages.Add(new { role = "user", content = prompt });
        }

        var payload = new
        {
            messages = messages,
            max_tokens = maxTokens,
            temperature = 0.0, // Alta precisão para laudos periciais
            top_p = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Enviando requisição para Azure OpenAI: {Deployment} ({ImageCount} imagem/imagens)",
            _settings.DeploymentName, images.Count);

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
