using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SinistroAPI.Data;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Models.Entities;

namespace SinistroAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Password == request.Password);

        if (user == null)
        {
            return Unauthorized(new LoginResponse(false, "Usuário ou senha incorretos"));
        }

        return Ok(new LoginResponse(true, "Login bem-sucedido", user.Username, user.Role));
    }

    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] LoginRequest request)
    {
        var existingUser = await _context.Users.AnyAsync(u => u.Username == request.Username);
        if (existingUser)
        {
            return BadRequest(new LoginResponse(false, "Este nome de usuário já está em uso"));
        }

        var newUser = new UserModel
        {
            Username = request.Username,
            Password = request.Password, // Ideally hashed, but keeping consistency with existing logic
            Role = "Membro" // Default safe role
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        return Ok(new LoginResponse(true, "Usuário registrado com sucesso. Aguarde a ativação pelo administrador."));
    }
}
