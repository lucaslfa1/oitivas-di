namespace SinistroAPI.Configuration;

/// <summary>
/// Mapeamento de prompts externos (prompts.json)
/// </summary>
public class PromptsOptions
{
    public string MasterForensicContext { get; set; } = "";
    public TranscricaoPrompts Transcricao { get; set; } = new();
    public RelatorioPrompts Relatorio { get; set; } = new();
    public VideoPrompts Video { get; set; } = new();
    public ExtrairDadosPrompts ExtrairDados { get; set; } = new();
    public string InstrucoesAgente { get; set; } = "";
}

public class TranscricaoPrompts
{
    public string Template { get; set; } = "";
    public string Formato { get; set; } = "";
}

public class RelatorioPrompts
{
    public string SystemPerito { get; set; } = "";
    public string SystemVistoria { get; set; } = "";
    public string PromptVistoriaImagem { get; set; } = "";
    public string PromptVistoriaMultiplas { get; set; } = "";
}

public class VideoPrompts
{
    public string Template { get; set; } = "";
}

public class ExtrairDadosPrompts
{
    public string Template { get; set; } = "";
}
