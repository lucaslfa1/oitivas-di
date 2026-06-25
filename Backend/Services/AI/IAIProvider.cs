namespace SinistroAPI.Services.AI;

/// <summary>
/// Interface de abstração para provedores de IA (Gemini, Vertex AI)
/// Permite trocar de provedor sem alterar os serviços consumidores
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Indica se o provedor está configurado e pronto para uso
    /// </summary>
    bool IsConfigured { get; }
    
    /// <summary>
    /// Nome do provedor para logging
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Gera conteúdo de texto com base em um prompt
    /// </summary>
    Task<string> GenerateTextAsync(string prompt, string? systemPrompt = null, float temperature = 0.0f);
    
    /// <summary>
    /// Analisa mídia (áudio, imagem, vídeo) com um prompt
    /// </summary>
    Task<string> AnalyzeMediaAsync(string prompt, byte[] mediaBytes, string mimeType, string? systemPrompt = null, float temperature = 0.3f);
    
    /// <summary>
    /// Analisa mídia grande via File API (upload e processamento)
    /// </summary>
    Task<string> AnalyzeLargeMediaAsync(string prompt, byte[] mediaBytes, string mimeType, string displayName, string? systemPrompt = null, float temperature = 0.3f);
}
