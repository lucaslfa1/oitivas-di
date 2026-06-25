using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de IMAGENS (fotos de vistoria veicular).
/// Utiliza Azure OpenAI (GPT-4o Vision) para análise de danos.
/// </summary>
public class ImagemAnaliseService
{
    private readonly ILogger<ImagemAnaliseService> _logger;
    private readonly AzureOpenAIService _azureOpenAI;
    private readonly PromptsOptions _prompts;

    public ImagemAnaliseService(
        ILogger<ImagemAnaliseService> logger,
        AzureOpenAIService azureOpenAI,
        IOptions<PromptsOptions> promptsOptions)
    {
        _logger = logger;
        _azureOpenAI = azureOpenAI;
        _prompts = promptsOptions.Value;
    }

    /// <summary>
    /// Verifica se o serviço está configurado
    /// </summary>
    public bool IsConfigured => _azureOpenAI.IsConfigured;

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
            throw new InvalidOperationException("Serviço de análise de imagem não está configurado (Azure OpenAI).");
        }

        _logger.LogInformation("Analisando imagem via Azure OpenAI (GPT-4o Vision) ({MimeType})", mimeType);

        var prompt = $"{GetVistoriaImagemPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}";
        return await _azureOpenAI.GenerateContentAsync(prompt, "", base64Image, mimeType);
    }

    /// <summary>
    /// Analisa múltiplas imagens de uma mesma vistoria
    /// </summary>
    public async Task<string> AnalisarMultiplasImagens(List<(string Base64, string MimeType)> imagens, string contextoUsuario)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Serviço de análise de imagem não está configurado (Azure OpenAI).");
        }

        _logger.LogInformation("Analisando {Count} imagens via Azure OpenAI (GPT-4o Vision)", imagens.Count);

        var prompt = $"{GetVistoriaMultiplasPrompt()}\n\nCONTEXTO DO USUÁRIO: {contextoUsuario}\n\nTotal de imagens: {imagens.Count}";
        return await _azureOpenAI.GenerateVisionAsync(prompt, "", imagens);
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
