using Google.Cloud.AIPlatform.V1;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço centralizado para interação com Vertex AI
/// Suporta autenticação via ADC (Application Default Credentials)
/// </summary>
public class VertexAIService
{
    private readonly VertexAISettings _settings;
    private readonly ILogger<VertexAIService> _logger;
    private readonly PredictionServiceClient _predictionClient;
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public VertexAIService(IOptions<VertexAISettings> settings, ILogger<VertexAIService> logger, HttpClient httpClient)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = httpClient;

        // Constrói o endpoint baseado na localização (ex: us-central1-aiplatform.googleapis.com)
        _endpoint = $"{_settings.Location}-aiplatform.googleapis.com";

        try
        {
            if (IsConfigured)
            {
                // Inicializa o cliente gRPC (usa ADC - Application Default Credentials)
                var builder = new PredictionServiceClientBuilder
                {
                    Endpoint = _endpoint
                };
                _predictionClient = builder.Build();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Vertex AI Client não pôde ser inicializado (provavelmente falta de ADC): {Message}", ex.Message);
        }
    }

    public bool IsConfigured => _settings.Enabled && !string.IsNullOrEmpty(_settings.ProjectId);

    /// <summary>
    /// Gera conteúdo usando um modelo específico do Vertex AI, opcionalmente com mídia
    /// </summary>
    public async Task<string> GenerateContentAsync(string modelId, string prompt, string? systemInstruction = null, string? base64Media = null, string? mimeType = null, float temperature = 0.0f)
    {
        if (!IsConfigured) throw new InvalidOperationException("Vertex AI não configurado (ProjectId ausente).");

        string modelResourceName;
        if (modelId.StartsWith("projects/"))
        {
            modelResourceName = modelId; 
        }
        else
        {
            modelResourceName = $"projects/{_settings.ProjectId}/locations/{_settings.Location}/publishers/google/models/{modelId}";
        }

        // Usa gRPC com ADC (Padrão correto para Vertex AI)
        return await GenerateContentGrpcAsync(modelResourceName, prompt, systemInstruction, base64Media, mimeType, temperature);
    }

    private async Task<string> GenerateContentGrpcAsync(string modelResourceName, string prompt, string? systemInstruction, string? base64Media, string? mimeType, float temperature)
    {
        var generateContentRequest = new GenerateContentRequest
        {
            Model = modelResourceName,
            GenerationConfig = new GenerationConfig
            {
                Temperature = temperature,
                MaxOutputTokens = 8192
            }
        };

        if (!string.IsNullOrEmpty(systemInstruction))
        {
            generateContentRequest.SystemInstruction = new Content
            {
                Role = "system",
                Parts = { new Part { Text = systemInstruction } }
            };
        }

        var userContent = new Content
        {
            Role = "user",
            Parts = { new Part { Text = prompt } }
        };

        if (!string.IsNullOrEmpty(base64Media) && !string.IsNullOrEmpty(mimeType))
        {
            userContent.Parts.Add(new Part
            {
                InlineData = new Blob
                {
                    MimeType = mimeType,
                    Data = Google.Protobuf.ByteString.FromBase64(base64Media)
                }
            });
        }

        generateContentRequest.Contents.Add(userContent);

        try
        {
            var response = await _predictionClient.GenerateContentAsync(generateContentRequest);
            
            if (response.Candidates.Count > 0 && response.Candidates[0].Content.Parts.Count > 0)
            {
                return response.Candidates[0].Content.Parts[0].Text;
            }
            
            return "Sem resposta do modelo.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar Vertex AI ({Model})", modelResourceName);
            throw;
        }
    }
}
