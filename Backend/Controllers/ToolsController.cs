using Microsoft.AspNetCore.Mvc;
using SinistroAPI.Interfaces;
using SinistroAPI.Models.Dtos;

namespace SinistroAPI.Controllers;

[ApiController]
[Route("api/tools")]
public class ToolsController : ControllerBase
{
    private readonly IMediaProcessorService _mediaProcessor;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        IMediaProcessorService mediaProcessor,
        ILogger<ToolsController> logger)
    {
        _mediaProcessor = mediaProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Faz o merge de múltiplos arquivos de áudio em um único arquivo WAV.
    /// É necessário enviar pelo menos 2 arquivos.
    /// </summary>
    [HttpPost("merge-audio")]
    public async Task<IActionResult> MergeAudio([FromForm] IFormFileCollection files)
    {
        // Aceita tanto "files" (padrão) quanto envio de múltiplos campos com qualquer nome
        if (files == null || files.Count < 2)
            return BadRequest(new ErrorResponse("É necessário enviar pelo menos 2 arquivos para realizar o merge."));

        _logger.LogInformation("Recebida solicitação de merge para {Count} arquivos", files.Count);

        try
        {
            var audioFiles = new List<byte[]>();
            string? commonMimeType = null;

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                // Usa o primeiro ContentType encontrado como base
                if (commonMimeType == null)
                    commonMimeType = file.ContentType;
                
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                audioFiles.Add(ms.ToArray());
            }

            if (audioFiles.Count < 2)
                return BadRequest(new ErrorResponse("É necessário enviar pelo menos 2 arquivos válidos."));

            // Assume que todos são do mesmo tipo (ou compatíveis) baseado no primeiro
            var result = await _mediaProcessor.MergeAudiosAsync(audioFiles, commonMimeType ?? "audio/mpeg");

            if (result == null)
            {
                _logger.LogError("MergeAudiosAsync retornou null para {Count} arquivos ({TotalKB} KB total, mime: {Mime})", 
                    audioFiles.Count, audioFiles.Sum(f => f.Length) / 1024, commonMimeType);
                return Problem("Falha ao processar o merge dos áudios. O serviço de processamento de mídia pode estar temporariamente indisponível. Tente novamente em alguns segundos.");
            }

            _logger.LogInformation("Merge realizado com sucesso. Retornando arquivo de {Size} bytes.", result.ProcessedBytes.Length);
            return File(result.ProcessedBytes, "audio/mpeg", "merged_audio.mp3");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no merge de áudio");
            return Problem("Erro interno no servidor: " + ex.Message);
        }
    }
}
