namespace SinistroAPI.Configuration;

/// <summary>
/// Configurações do MediaProcessor (Python microservice)
/// </summary>
public class MediaProcessorSettings
{
    public const string SectionName = "MediaProcessor";
    
    /// <summary>
    /// URL base do microserviço Python (ex: http://localhost:8000)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";
    
    /// <summary>
    /// Timeout em segundos para chamadas ao processor
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
    
    /// <summary>
    /// Se o pré-processamento está habilitado
    /// </summary>
    public bool Enabled { get; set; } = true;
}
