namespace SinistroAPI.Configuration;

/// <summary>
/// Configurações de resiliência para chamadas de API
/// </summary>
public class ResilienceOptions
{
    public int GeminiTimeoutMinutes { get; set; } = 15;
    public int MaxInlineUploadMB { get; set; } = 15;
    public int MaxRetries { get; set; } = 3;
}
