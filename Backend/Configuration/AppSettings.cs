// Sentinel - Configurações centralizadas da aplicação

namespace SinistroAPI.Configuration;

/// <summary>
/// Configurações do Vertex AI e Modelos
/// </summary>
public class VertexAISettings
{
    public const string SectionName = "VertexAI";
    public bool Enabled { get; set; } = false; // Feature flag para controlar uso do Vertex
    public string ProjectId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Location { get; set; } = "us-central1";
    public ModelSettings Models { get; set; } = new();
}

/// <summary>
/// Configurações de modelos de IA
/// </summary>
public class ModelSettings
{
    public string Transcription { get; set; } = "gemini-1.5-pro";
    public string ImageAnalysis { get; set; } = "gemini-1.5-pro";
    public string VideoAnalysis { get; set; } = "gemini-1.5-pro";
    public string ReportGeneration { get; set; } = "gemini-1.5-pro-002";
}

/// <summary>
/// Configurações de CORS
/// </summary>
public class CorsSettings
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

