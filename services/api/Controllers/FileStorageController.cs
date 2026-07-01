using Microsoft.AspNetCore.Mvc;

namespace SinistroAPI.Controllers;

[ApiController]
[Route("api/storage")]
public class FileStorageController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileStorageController> _logger;

    public FileStorageController(IWebHostEnvironment env, ILogger<FileStorageController> logger)
    {
        _env = env;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Arquivo inválido");

        try
        {
            // Define pasta de destino
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "audio");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // Gera nome único
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            // Salva arquivo
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Retorna URL relativa
            var relativeUrl = $"/uploads/audio/{fileName}";
            
            _logger.LogInformation("Arquivo salvo em: {Path}", filePath);

            return Ok(new { url = relativeUrl, fileName = file.FileName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar arquivo");
            return StatusCode(500, "Erro interno ao salvar arquivo");
        }
    }
}
