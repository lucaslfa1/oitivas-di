namespace SinistroAPI.Models.Dtos;

/// <summary>
/// DTO para upload de arquivos (áudio, imagem, vídeo)
/// </summary>
public class UploadDto
{
    /// <summary>
    /// Arquivo enviado pelo usuário
    /// </summary>
    public IFormFile? Arquivo { get; set; }
    
    /// <summary>
    /// Contexto adicional fornecido pelo usuário
    /// </summary>
    public string? Contexto { get; set; }
    
    /// <summary>
    /// Modo de análise (audio, foto, video)
    /// </summary>
    public string? Modo { get; set; }
    
    /// <summary>
    /// Duração do arquivo (para áudio/vídeo)
    /// </summary>
    public string? Duracao { get; set; }
    
    /// <summary>
    /// Transcrição já existente (para geração de laudo)
    /// </summary>
    public string? Transcricao { get; set; }
}
