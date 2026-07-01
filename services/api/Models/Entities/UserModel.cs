namespace SinistroAPI.Models.Entities;

public class UserModel
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty; // In production, use hashing
    public string Role { get; set; } = string.Empty; // Admin, Coordenador, Supervisor, Analista, Operador
}
