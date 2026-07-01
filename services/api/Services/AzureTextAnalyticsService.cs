using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SinistroAPI.Models;

namespace SinistroAPI.Services;

public interface IAzureTextAnalyticsService
{
    bool IsConfigured { get; }
    Task<SentimentResult?> AnalyzeSentimentAsync(string transcriptText);
}

public class AzureTextAnalyticsService : IAzureTextAnalyticsService
{
    private readonly TextAnalyticsClient? _client;
    private readonly ILogger<AzureTextAnalyticsService> _logger;
    private readonly bool _isEnabled;

    public AzureTextAnalyticsService(IConfiguration configuration, ILogger<AzureTextAnalyticsService> logger)
    {
        _logger = logger;
        
        var key = configuration["AzureTextAnalytics:Key"];
        var endpoint = configuration["AzureTextAnalytics:Endpoint"];
        _isEnabled = configuration.GetValue<bool>("AzureTextAnalytics:Enabled", true);

        if (_isEnabled && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(endpoint))
        {
            try
            {
                var credentials = new AzureKeyCredential(key);
                var endpointUri = new Uri(endpoint);
                _client = new TextAnalyticsClient(endpointUri, credentials);
                _logger.LogInformation("Azure Text Analytics Service configurado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao configurar Azure Text Analytics: {Error}", ex.Message);
            }
        }
    }

    public bool IsConfigured => _isEnabled && _client != null;

    public async Task<SentimentResult?> AnalyzeSentimentAsync(string transcriptText)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(transcriptText))
            return null;

        try
        {
            _logger.LogInformation("Iniciando analise de sentimento do texto na Azure...");

            // Limitar texto se muito longo, mas API da Azure aguenta grandes pedacos.
            DocumentSentiment documentSentiment = await _client.AnalyzeSentimentAsync(transcriptText, language: "pt-BR");

            var result = new SentimentResult
            {
                Classification = MapearParaClassificacaoNossa(documentSentiment.Sentiment),
                Description = "Sensacao geral baseada no relato escrito.",
                Metrics = new Dictionary<string, double>
                {
                    { "positive_score", documentSentiment.ConfidenceScores.Positive },
                    { "neutral_score", documentSentiment.ConfidenceScores.Neutral },
                    { "negative_score", documentSentiment.ConfidenceScores.Negative }
                }
            };
            
            _logger.LogInformation("Analise de sentimento Azure concluida: {Classe}", result.Classification);
            return result;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError("Erro na chamada da API Azure Text Analytics: {Status} {Error}", ex.Status, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("Erro inesperado durante analise de sentimento: {Error}", ex.Message);
            return null;
        }
    }

    private string MapearParaClassificacaoNossa(TextSentiment azureSentiment)
    {
        // Nostos tipos atuais (da versao python): calma, tensao, hesitação, agressividade, confusao.
        // A Azure devolve: Positive, Neutral, Negative, Mixed.
        return azureSentiment switch
        {
            TextSentiment.Positive => "calma", 
            TextSentiment.Negative => "tensao", // Assumimos tensao/choque em relatos criminais
            TextSentiment.Mixed => "confusao",
            _ => "neutro"
        };
    }
}
