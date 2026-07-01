using Microsoft.AspNetCore.Mvc;
using SinistroAPI.Data;
using SinistroAPI.Services;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controller para health check e diagnósticos
/// </summary>
[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MediaProcessorService _mediaProcessor;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        AppDbContext db, 
        MediaProcessorService mediaProcessor,
        ILogger<HealthController> logger)
    {
        _db = db;
        _mediaProcessor = mediaProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Verifica status completo da aplicação
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var dbHealthy = false;
        var dbMessage = "unknown";
        var pythonAvailable = false;
        var pythonMessage = "disabled";

        // Verificar banco de dados
        try
        {
            dbHealthy = await _db.Database.CanConnectAsync();
            dbMessage = dbHealthy ? "connected" : "disconnected";
        }
        catch (Exception ex)
        {
            dbMessage = $"error: {ex.Message}";
        }

        // Verificar Python Processor
        if (_mediaProcessor.IsEnabled)
        {
            try
            {
                pythonAvailable = await _mediaProcessor.IsAvailableAsync();
                pythonMessage = pythonAvailable ? "healthy" : "unavailable";
            }
            catch (Exception ex)
            {
                pythonMessage = $"error: {ex.Message}";
                _logger.LogWarning("Health check Python failed: {Error}", ex.Message);
            }
        }

        // Status geral: DB é crítico, Python não (tem fallback)
        var overallStatus = dbHealthy ? "healthy" : "unhealthy";
        if (dbHealthy && _mediaProcessor.IsEnabled && !pythonAvailable)
        {
            overallStatus = "degraded";
        }

        var response = new
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow.ToString("o"),
            services = new
            {
                database = new { status = dbMessage, healthy = dbHealthy },
                pythonProcessor = new
                {
                    status = pythonMessage,
                    enabled = _mediaProcessor.IsEnabled,
                    available = pythonAvailable
                }
            }
        };

        return dbHealthy ? Ok(response) : StatusCode(503, response);
    }

    /// <summary>
    /// Liveness probe para Kubernetes/Cloud Run
    /// </summary>
    [HttpGet("health/live")]
    public IActionResult Liveness() => Ok(new { status = "alive" });

    /// <summary>
    /// Readiness probe - ready quando DB conectado
    /// </summary>
    [HttpGet("health/ready")]
    public async Task<IActionResult> Readiness()
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync();
            return canConnect 
                ? Ok(new { status = "ready" }) 
                : StatusCode(503, new { status = "not ready" });
        }
        catch
        {
            return StatusCode(503, new { status = "not ready" });
        }
    }
}
