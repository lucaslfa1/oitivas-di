namespace SinistroAPI.Models.Dtos;

/// <summary>
/// Resposta padrão para transcrição
/// </summary>
public record TranscricaoResponse(string Transcricao, string Fonte);

/// <summary>
/// Resposta padrão para análise/laudo
/// </summary>
public record AnaliseResponse(string Markdown, string? Fonte = null);

/// <summary>
/// Resposta padrão para operações CRUD
/// </summary>
public record OperacaoResponse(int? Id, string Msg);

/// <summary>
/// Resposta de erro padrão
/// </summary>
public record ErrorResponse(string Error);

/// <summary>
/// Resposta do health check
/// </summary>
public record HealthResponse(string Status, string Database, DateTime Timestamp, string? Error = null);
