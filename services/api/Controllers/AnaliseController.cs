using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Services;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controller para análise de mídias (imagens e vídeos)
/// </summary>
[ApiController]
[Route("api/analisar")]
public class AnaliseController : ControllerBase
{
    private readonly ImagemAnaliseService _imagemService;
    private readonly VideoAnaliseService _videoService;
    private readonly ILogger<AnaliseController> _logger;
    private readonly UploadLimitsOptions _uploadLimits;

    public AnaliseController(
        ImagemAnaliseService imagemService,
        VideoAnaliseService videoService,
        IOptions<UploadLimitsOptions> uploadLimits,
        ILogger<AnaliseController> logger)
    {
        _imagemService = imagemService;
        _videoService = videoService;
        _uploadLimits = uploadLimits.Value;
        _logger = logger;
    }

    /// <summary>
    /// Analisa imagem de vistoria veicular
    /// </summary>
    [HttpPost("imagem")]
    public async Task<IActionResult> AnalisarImagem([FromForm] UploadDto dados)
    {
        if (dados.Arquivo == null || dados.Arquivo.Length == 0)
            return BadRequest(new ErrorResponse("Nenhum arquivo."));

        if (!dados.Arquivo.ContentType.StartsWith("image/"))
            return BadRequest(new ErrorResponse("Arquivo deve ser uma imagem."));

        if (dados.Arquivo.Length > _uploadLimits.MaxImageUploadBytes)
            return StatusCode(413, new ErrorResponse("Arquivo de imagem excede o limite configurado."));

        try
        {
            if (!_imagemService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Serviço de imagem não configurado."));
            }

            using var ms = new MemoryStream();
            await dados.Arquivo.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var laudo = await _imagemService.AnalisarImagem(base64, dados.Arquivo.ContentType, dados.Contexto ?? "");
            return Ok(new AnaliseResponse(laudo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na análise de imagem");
            return Problem("Falha na análise de imagem: " + ex.Message);
        }
    }

    /// <summary>
    /// Analisa vídeo de sinistro (suporta até 2GB via File API)
    /// </summary>
    [HttpPost("video")]
    public async Task<IActionResult> AnalisarVideo([FromForm] UploadDto dados)
    {
        if (dados.Arquivo == null || dados.Arquivo.Length == 0)
            return BadRequest(new ErrorResponse("Nenhum arquivo."));

        if (!dados.Arquivo.ContentType.StartsWith("video/"))
            return BadRequest(new ErrorResponse("Arquivo deve ser um vídeo."));

        try
        {
            if (!_videoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Serviço de vídeo não configurado (Azure OpenAI)."));
            }

            _logger.LogInformation("Analisando vídeo: {Size} MB", dados.Arquivo.Length / 1024.0 / 1024.0);

            using var stream = dados.Arquivo.OpenReadStream();
            var laudo = await _videoService.AnalisarVideoStream(
                stream,
                dados.Arquivo.ContentType,
                dados.Arquivo.FileName,
                dados.Contexto ?? "",
                dados.Duracao ?? ""
            );

            return Ok(new AnaliseResponse(laudo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na análise de vídeo");
            return Problem("Falha na análise de vídeo: " + ex.Message);
        }
    }
}
