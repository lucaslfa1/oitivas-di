namespace SinistroAPI.Models.Entities;

/// <summary>
/// Entidade para análises salvas no banco de dados
/// </summary>
public class AnaliseModel
{
    public int Id { get; set; }
    
    /// <summary>
    /// Tipo de análise: Oitiva, Vistoria, Vídeo, Transcrição
    /// </summary>
    public string? Tipo { get; set; }
    
    /// <summary>
    /// Conteúdo em Markdown do laudo/transcrição
    /// </summary>
    public string? Conteudo { get; set; }
    
    /// <summary>
    /// Nome do arquivo original
    /// </summary>
    public string? Arquivo { get; set; }
    
    /// <summary>
    /// Data da análise
    /// </summary>
    public DateTime Data { get; set; }
}
