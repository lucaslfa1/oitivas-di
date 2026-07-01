namespace SinistroAPI.Models.Dtos;

/// <summary>
/// DTO para solicitações de auditoria de conformidade
/// </summary>
public record AuditoriaDto(
    string Transcricao,
    string? Roteiro = null,
    string? Contexto = null
);
