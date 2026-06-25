namespace SinistroAPI.Models.Dtos;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    bool Success, 
    string? Message = null, 
    string? Username = null, 
    string? Role = null
);
