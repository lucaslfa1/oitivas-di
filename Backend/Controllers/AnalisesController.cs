using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SinistroAPI.Data;
using SinistroAPI.Models.Entities;
using SinistroAPI.Models.Dtos;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controller para gerenciamento de análises salvas
/// </summary>
[ApiController]
[Route("api")]
public class AnalisesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnalisesController> _logger;

    public AnalisesController(AppDbContext db, ILogger<AnalisesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Salva uma nova análise
    /// </summary>
    [HttpPost("salvar")]
    public async Task<IActionResult> Salvar([FromBody] AnaliseModel model)
    {
        model.Data = DateTime.Now;
        _db.Analises.Add(model);
        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Análise salva com ID: {Id}", model.Id);
        return Ok(new OperacaoResponse(model.Id, "Análise salva com sucesso!"));
    }

    /// <summary>
    /// Lista as últimas 50 análises
    /// </summary>
    [HttpGet("analises")]
    public async Task<IActionResult> Listar()
    {
        var analises = await _db.Analises
            .OrderByDescending(a => a.Data)
            .Take(50)
            .ToListAsync();
            
        return Ok(analises);
    }

    /// <summary>
    /// Busca análise por ID
    /// </summary>
    [HttpGet("analises/{id}")]
    public async Task<IActionResult> BuscarPorId(int id)
    {
        var analise = await _db.Analises.FindAsync(id);
        return analise == null ? NotFound() : Ok(analise);
    }

    /// <summary>
    /// Deleta uma análise
    /// </summary>
    [HttpDelete("analises/{id}")]
    public async Task<IActionResult> Deletar(int id)
    {
        var analise = await _db.Analises.FindAsync(id);
        if (analise == null) return NotFound();
        
        _db.Analises.Remove(analise);
        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Análise {Id} deletada", id);
        return Ok(new OperacaoResponse(null, "Deletado com sucesso"));
    }
}
