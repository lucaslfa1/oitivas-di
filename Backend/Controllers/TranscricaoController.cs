using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Interfaces;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Services;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controller para transcrição de áudio e geração de laudos
/// </summary>
[ApiController]
[Route("api")]
public class TranscricaoController : ControllerBase
{
    private readonly ITranscricaoService _transcricaoService;
    private readonly IDescricaoAnaliseService _descricaoService;
    private readonly IMediaProcessorService _mediaProcessor;
    private readonly IAzureTextAnalyticsService _azureSentiment;
    private readonly ILogger<TranscricaoController> _logger;
    private readonly UploadLimitsOptions _uploadLimits;

    public TranscricaoController(
        ITranscricaoService transcricaoService,
        IDescricaoAnaliseService descricaoService,
        IMediaProcessorService mediaProcessor,
        IAzureTextAnalyticsService azureSentiment,
        IOptions<UploadLimitsOptions> uploadLimits,
        ILogger<TranscricaoController> logger)
    {
        _transcricaoService = transcricaoService;
        _descricaoService = descricaoService;
        _mediaProcessor = mediaProcessor;
        _azureSentiment = azureSentiment;
        _uploadLimits = uploadLimits.Value;
        _logger = logger;
    }

    /// <summary>
    /// Transcreve áudio de oitiva
    /// </summary>
    [HttpPost("transcrever")]
    public async Task<IActionResult> Transcrever([FromForm] UploadDto dados)
    {
        if (dados.Arquivo == null || dados.Arquivo.Length == 0)
            return BadRequest(new ErrorResponse("Nenhum arquivo."));

        if (!dados.Arquivo.ContentType.StartsWith("audio/") && 
            !dados.Arquivo.ContentType.StartsWith("video/webm") && 
            !dados.Arquivo.ContentType.StartsWith("video/mp4"))
            return BadRequest(new ErrorResponse("Formato inválido. Envie áudio ou vídeo (webm/mp4)."));

        if (dados.Arquivo.Length > _uploadLimits.MaxAudioUploadBytes)
            return StatusCode(413, new ErrorResponse("Arquivo de audio excede o limite configurado."));

        try
        {
            if (!_transcricaoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Nenhum serviço de transcrição (Azure-only) configurado."));
            }

            using var ms = new MemoryStream();
            await dados.Arquivo.CopyToAsync(ms);
            var audioBytes = ms.ToArray();
            var mimeType = dados.Arquivo.ContentType;

            _logger.LogInformation("Iniciando transcrição de áudio ({Size} MB)", audioBytes.Length / 1024.0 / 1024.0);
            
            string? connectionId = Request.Headers["X-Connection-Id"];
            
            // 1. STT (Speech to Text)
            var transcricao = await _transcricaoService.TranscreverAudio(audioBytes, mimeType, connectionId);

            // 2. Disparar análise de sentimento em background
            // Fase 7: Usar Texto da Transcricao (Azure Language Analytics) -> Fallback (Python Acoustic) se falhar
            _ = Task.Run(async () => 
            {
                try
                {
                    SentimentResult? sentiment = null;
                    
                    if (_azureSentiment.IsConfigured && !string.IsNullOrWhiteSpace(transcricao))
                    {
                        sentiment = await _azureSentiment.AnalyzeSentimentAsync(transcricao);
                    }

                    if (sentiment == null)
                    {
                        _logger.LogWarning("Fallback: Analise Azure Sentiment falhou ou inativa. Usando analise acustica do Python.");
                        sentiment = await _mediaProcessor.AnalyzeSentimentAsync(audioBytes, mimeType);
                    }

                    if (sentiment != null)
                    {
                        _logger.LogInformation("Analise de Sentimento Concluida: {Classification} - {Description}", 
                            sentiment.Classification, sentiment.Description);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Erro na analise de sentimento em background: {Error}", ex.Message);
                }
            });

            return Ok(new TranscricaoResponse(transcricao, "azure"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na transcrição de áudio");
            return Problem("Falha na transcrição: " + ex.Message);
        }
    }

    /// <summary>
    /// Gera laudo pericial a partir da transcrição
    /// </summary>
    [HttpPost("analisar/laudo")]
    public async Task<IActionResult> GerarLaudo([FromBody] OitivaDto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Transcricao))
            return BadRequest(new ErrorResponse("Transcrição não fornecida."));

        try
        {
            if (!_descricaoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Nenhum serviço de IA configurado. Configure Azure OpenAI (endpoint, deployment e chave)."));
            }

            _logger.LogInformation("Gerando laudo pericial");
            var laudo = await _descricaoService.AnalisarTranscricaoOitiva(
                dados.Transcricao,
                dados.Duracao ?? "Não informada",
                dados.Contexto ?? ""
            );

            return Ok(new AnaliseResponse(laudo, "azure-openai"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar laudo pericial");
            return Problem("Falha ao gerar laudo: " + ex.Message);
        }
    }

    /// <summary>
    /// Analisa transcrição de oitiva
    /// </summary>
    [HttpPost("analisar/oitiva")]
    public async Task<IActionResult> AnalisarOitiva([FromBody] OitivaDto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Transcricao))
            return BadRequest(new ErrorResponse("Transcrição não fornecida."));

        try
        {
            if (!_descricaoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Serviço de descrição não configurado."));
            }

            var laudo = await _descricaoService.AnalisarTranscricaoOitiva(
                dados.Transcricao, 
                dados.Duracao ?? "Não informada", 
                dados.Contexto ?? ""
            );
            
            return Ok(new AnaliseResponse(laudo));
        }
        catch (Exception ex)
        {
            return Problem("Falha ao analisar oitiva: " + ex.Message);
        }
    }

    /// <summary>
    /// Audita a conformidade de uma transcrição com base em um roteiro
    /// </summary>
    [HttpPost("auditar")]
    public async Task<IActionResult> Auditar([FromBody] AuditoriaDto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Transcricao))
            return BadRequest(new ErrorResponse("Transcrição não fornecida."));

        try
        {
            if (!_descricaoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Serviço de IA não configurado."));
            }

            _logger.LogInformation("Auditoria de conformidade em andamento");
            var roteiro = dados.Roteiro ?? "Padrao de Oitivas Opentech (Identificacao, Fatos, Conclusao)";
            var auditoria = await _descricaoService.AuditarConformidade(dados.Transcricao, roteiro);

            return Ok(new AnaliseResponse(auditoria, "azure-gpt4o"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na auditoria de conformidade");
            return Problem("Falha na auditoria: " + ex.Message);
        }
    }

    /// <summary>
    /// Extrai dados estruturados (JSON) da transcrição
    /// </summary>
    [HttpPost("extrair-dados")]
    public async Task<IActionResult> ExtrairDados([FromBody] OitivaDto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Transcricao))
            return BadRequest(new ErrorResponse("Transcrição não fornecida."));

        try
        {
            var dadosExtraidos = await _transcricaoService.ExtrairDadosOitiva(dados.Transcricao);
            return Ok(dadosExtraidos);
        }
        catch (Exception ex)
        {
            return Problem("Falha na extração de dados: " + ex.Message);
        }
    }
}

