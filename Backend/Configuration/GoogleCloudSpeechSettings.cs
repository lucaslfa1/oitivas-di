namespace SinistroAPI.Configuration;

/// <summary>
/// Configurações do Google Cloud Speech-to-Text
/// </summary>
public class GoogleCloudSpeechSettings
{
    public const string SectionName = "GoogleCloudSpeech";

    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// "ServiceAccount" (default/recommended) or "ApiKey"
    /// </summary>
    public string AuthMethod { get; set; } = "ServiceAccount";

    /// <summary>
    /// Caminho para o arquivo JSON de credenciais (opcional se usar ADC)
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Necessário apenas se AuthMethod="ApiKey"
    /// </summary>
    public string? QuotaProjectId { get; set; }

    public string LanguageCode { get; set; } = "pt-BR";
}
