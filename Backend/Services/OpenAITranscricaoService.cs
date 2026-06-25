using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SinistroAPI.Interfaces;

namespace SinistroAPI.Services;

public class OpenAITranscricaoService : ITranscricaoService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<OpenAITranscricaoService> _logger;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public OpenAITranscricaoService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAITranscricaoService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"];
        _logger = logger;
    }

    public async Task<string> TranscreverAudio(byte[] audioBytes, string mimeType, string? connectionId = null)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("API Key da OpenAI não configurada.");
        }

        _logger.LogInformation("Iniciando transcrição com OpenAI Whisper...");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var content = new MultipartFormDataContent();
        
        // Adiciona o arquivo de áudio
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        content.Add(audioContent, "file", "audio.mp3"); // Whisper aceita mp3, wav, etc. O nome do arquivo é obrigatório mas pode ser genérico.

        // Adiciona o modelo
        content.Add(new StringContent("whisper-1"), "model");
        
        // Opcional: idioma (se soubermos que é pt)
        content.Add(new StringContent("pt"), "language");

        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro na API OpenAI: {Error}", error);
                throw new Exception($"Erro na API OpenAI: {error}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString() ?? "";
                _logger.LogInformation("Transcrição OpenAI concluída ({Length} chars)", text.Length);
                return text;
            }
            
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError("Falha ao chamar OpenAI: {Message}", ex.Message);
            throw;
        }
    }

    public Task<Dictionary<string, string>> ExtrairDadosOitiva(string transcricao)
    {
        // Por enquanto, não implementado para OpenAI, retorna vazio ou pode delegar serviço
        return Task.FromResult(new Dictionary<string, string>());
    }
}
