using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de IMAGENS (fotos de vistoria veicular)
/// Utiliza Gemini 2.5 Pro para análise de danos
/// </summary>
public class ImagemAnaliseService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<ImagemAnaliseService> _logger;
    private readonly VertexAIService _vertexAI;
    private readonly VertexAISettings _vertexSettings;
    private readonly string _imageAnalysisModel;
    private readonly PromptsOptions _prompts;
    private readonly AzureOpenAIService _azureOpenAI;

    public ImagemAnaliseService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<ImagemAnaliseService> logger, 
        VertexAIService vertexAI, 
        AzureOpenAIService azureOpenAI,
        IOptions<VertexAISettings> vertexSettings,
        IOptions<PromptsOptions> promptsOptions)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _logger = logger;
        _vertexAI = vertexAI;
        _azureOpenAI = azureOpenAI;
        _vertexSettings = vertexSettings.Value;
        _imageAnalysisModel = _vertexSettings.Models.ImageAnalysis ?? "gemini-1.5-pro";
        _prompts = promptsOptions.Value;
    }

    /// <summary>
    /// Verifica se o serviço está configurado
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) || _vertexAI.IsConfigured || _azureOpenAI.IsConfigured;

    /// <summary>
    /// Analisa imagem de vistoria veicular
    /// </summary>
    /// <param name="base64Image">Imagem em base64</param>
    /// <param name="mimeType">Tipo MIME (image/jpeg, image/png, etc.)</param>
    /// <param name="contextoUsuario">Contexto adicional fornecido pelo usuário</param>
    /// <returns>Laudo de vistoria em markdown</returns>
    public async Task<string> AnalisarImagem(string base64Image, string mimeType, string contextoUsuario)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de imagem não está configurado.");
        }

        _logger.LogInformation("Iniciando análise de imagem ({MimeType})", mimeType);

        if (_azureOpenAI.IsConfigured)
        {
            _logger.LogInformation("Analisando via Azure OpenAI (GPT-4o Vision)...");
            return await _azureOpenAI.GenerateContentAsync($"{GetVistoriaImagemPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}", "", base64Image);
        }

        if (_vertexAI.IsConfigured)
        {
            return await _vertexAI.GenerateContentAsync(_vertexSettings.Models.ImageAnalysis, $"{GetVistoriaImagemPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}", null, base64Image, mimeType);
        }

        var payload = new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"{GetVistoriaImagemPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}" },
                        new {
                            inline_data = new {
                                mime_type = mimeType,
                                data = base64Image
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 4096
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_imageAnalysisModel}:generateContent?key={_apiKey}";
        
        var resposta = await _httpClient.PostAsync(url, jsonContent);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync();
            _logger.LogError("Erro Gemini na análise de imagem: {StatusCode} - {Erro}", resposta.StatusCode, erro);
            throw new Exception($"Erro na análise de imagem ({resposta.StatusCode}): {erro}");
        }

        var respostaString = await resposta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respostaString);
        
        try 
        {
            var resultado = doc.RootElement.GetProperty("candidates")[0]
                                  .GetProperty("content")
                                  .GetProperty("parts")[0]
                                  .GetProperty("text")
                                  .GetString() ?? "Sem resposta.";
            
            _logger.LogInformation("Análise de imagem concluída com sucesso");
            return resultado;
        }
        catch
        {
            _logger.LogError("Erro ao processar resposta da análise de imagem");
            return "Erro ao processar o laudo de vistoria.";
        }
    }

    /// <summary>
    /// Analisa múltiplas imagens de uma mesma vistoria
    /// </summary>
    public async Task<string> AnalisarMultiplasImagens(List<(string Base64, string MimeType)> imagens, string contextoUsuario)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de imagem não está configurado.");
        }

        _logger.LogInformation("Iniciando análise de {Count} imagens", imagens.Count);

        // Vertex AI implementation for multiple images would go here, but for now we'll stick to Gemini fallback or implement if needed.
        // Since VertexAIService currently supports single media item, we might need to extend it or loop.
        // For simplicity, let's keep Gemini logic here for multiple images or extend VertexAIService later.
        // Assuming the requirement is mainly for single image analysis as per current usage.
        
        // TODO: Implement Vertex AI support for multiple images if required.

        var parts = new List<object>
        {
            new { text = $"{GetVistoriaMultiplasPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}\n\nTotal de imagens: {imagens.Count}" }
        };

        foreach (var (base64, mimeType) in imagens)
        {
            parts.Add(new {
                inline_data = new {
                    mime_type = mimeType,
                    data = base64
                }
            });
        }

        var payload = new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = parts.ToArray()
                }
            },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 8192
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_imageAnalysisModel}:generateContent?key={_apiKey}";
        
        var resposta = await _httpClient.PostAsync(url, jsonContent);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync();
            throw new Exception($"Erro na análise de imagens ({resposta.StatusCode}): {erro}");
        }

        var respostaString = await resposta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respostaString);
        
        return doc.RootElement.GetProperty("candidates")[0]
                              .GetProperty("content")
                              .GetProperty("parts")[0]
                              .GetProperty("text")
                              .GetString() ?? "Sem resposta.";
    }

    /// <summary>
    /// Monta o prompt de vistoria de imagem única usando configuração externa
    /// </summary>
    private string GetVistoriaImagemPrompt()
    {
        return $"{_prompts.MasterForensicContext}\n\n{_prompts.Relatorio.PromptVistoriaImagem}";
    }

    /// <summary>
    /// Monta o prompt de vistoria de múltiplas imagens usando configuração externa
    /// </summary>
    private string GetVistoriaMultiplasPrompt()
    {
        return $"{_prompts.MasterForensicContext}\n\n{_prompts.Relatorio.PromptVistoriaMultiplas}";
    }
}
