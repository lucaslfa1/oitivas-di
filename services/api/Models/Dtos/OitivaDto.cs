namespace SinistroAPI.Models.Dtos;

/// <summary>
/// DTO para análise de oitiva/laudo
/// </summary>
public class OitivaDto
{
    /// <summary>
    /// Texto da transcrição a ser analisada
    /// </summary>
    public string? Transcricao { get; set; }
    
    /// <summary>
    /// Duração do áudio original
    /// </summary>
    public string? Duracao { get; set; }
    
    /// <summary>
    /// Contexto adicional fornecido pelo usuário
    /// </summary>
    public string? Contexto { get; set; }
}
