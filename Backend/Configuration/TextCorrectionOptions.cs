namespace SinistroAPI.Configuration;

/// <summary>
/// Mapeamento de correções de texto (text_corrections.json)
/// </summary>
public class TextCorrectionOptions
{
    public string Target { get; set; } = "";
    public List<string> Patterns { get; set; } = new();
}

/// <summary>
/// Wrapper para a lista de correções (necessário para IOptions)
/// </summary>
public class TextCorrectionsWrapper
{
    public List<TextCorrectionOptions> Corrections { get; set; } = new();
}
