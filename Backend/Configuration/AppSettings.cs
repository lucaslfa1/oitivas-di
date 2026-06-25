// Sentinel - Configurações centralizadas da aplicação

namespace SinistroAPI.Configuration;

/// <summary>
/// Configurações de CORS
/// </summary>
public class CorsSettings
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

