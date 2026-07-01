using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Interfaces;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Services;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controller REST do Sentinel para o fluxo forense de oitivas: transcreve áudio/vídeo (STT),
/// gera laudo pericial, analisa, audita conformidade e extrai dados estruturados da transcrição.
/// </summary>
/// <remarks>
/// COMO funciona (visão geral do módulo):
/// 1. Todos os endpoints ficam sob o prefixo de rota "api" (atributo [Route("api")] na classe).
/// 2. O controller é um orquestrador fino: ele apenas valida a entrada (HTTP 400/413), delega o
///    trabalho pesado aos serviços injetados via DI e converte exceções em respostas HTTP (Problem/500).
/// 3. A stack de IA é Azure-only. Os serviços usados são:
///    - <see cref="ITranscricaoService"/>: STT via Azure Speech + extração de dados estruturados.
///    - <see cref="IDescricaoAnaliseService"/>: geração de laudo/análise/auditoria via Azure OpenAI (GPT-4o).
///    - <see cref="IAzureTextAnalyticsService"/>: análise de sentimento textual (Azure Language).
///    - <see cref="IMediaProcessorService"/>: serviço Python auxiliar (análise acústica), usado como
///      fallback quando o sentimento textual do Azure não está disponível.
/// 4. Cada serviço expõe a flag IsConfigured; o controller checa essa flag antes de chamar para
///    devolver um 400 explicativo em vez de estourar uma exceção quando faltam credenciais.
/// </remarks>
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

    /// <summary>
    /// Injeta, via DI, os serviços de IA Azure-only, os limites de upload e o logger.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// - Os parâmetros são preenchidos pelo container de DI do ASP.NET Core; cada interface tem uma
    ///   implementação concreta registrada na inicialização (Program.cs/Startup).
    /// - <paramref name="uploadLimits"/> chega como <see cref="IOptions{TOptions}"/> (padrão Options do
    ///   .NET); guardamos apenas o <c>.Value</c> (snapshot da configuração) para uso direto nos endpoints.
    /// </remarks>
    /// <param name="transcricaoService">Serviço de STT (Azure Speech) e extração de dados da oitiva.</param>
    /// <param name="descricaoService">Serviço de laudo/análise/auditoria (Azure OpenAI GPT-4o).</param>
    /// <param name="mediaProcessor">Serviço Python de análise acústica (fallback de sentimento).</param>
    /// <param name="azureSentiment">Serviço de análise de sentimento textual (Azure Language).</param>
    /// <param name="uploadLimits">Limites de upload configurados (ex.: tamanho máximo de áudio).</param>
    /// <param name="logger">Logger tipado deste controller.</param>
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
    /// Recebe um arquivo de áudio/vídeo (multipart/form-data) e devolve a transcrição (STT) da oitiva.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Validação de presença: rejeita (400) se não houver arquivo ou se ele estiver vazio (Length == 0).
    /// 2. Validação de mimetype (whitelist): só aceita Content-Type começando com "audio/", "video/webm"
    ///    ou "video/mp4". O navegador grava do microfone tipicamente como "audio/webm" ou "video/webm";
    ///    por isso o webm aparece nas duas famílias. Qualquer outro formato retorna 400.
    /// 3. Validação de tamanho: se exceder <c>_uploadLimits.MaxAudioUploadBytes</c> (limite configurável),
    ///    retorna 413 (Payload Too Large) em vez de 400, pois semanticamente o pedido é válido mas grande
    ///    demais. Isso protege memória/banda antes de carregar o arquivo todo em memória no passo 5.
    /// 4. Checagem de configuração: se nenhum provedor de STT (Azure-only) estiver configurado
    ///    (<c>IsConfigured == false</c>), retorna 400 explicativo em vez de falhar mais adiante.
    /// 5. Carrega o arquivo inteiro em um byte[] via MemoryStream (o STT trabalha sobre os bytes brutos).
    ///    O log registra o tamanho em MB (bytes / 1024 / 1024) apenas para observabilidade.
    /// 6. Lê o header opcional "X-Connection-Id": é o id da conexão SignalR/WebSocket do cliente, usado
    ///    pelo serviço de STT para enviar atualizações de progresso em tempo real ao usuário correto.
    ///    É opcional (string?); quando ausente, a transcrição roda sem streaming de progresso.
    /// 7. Executa o STT de forma síncrona (await) e retorna a transcrição com o provedor "azure".
    /// 8. ANÁLISE DE SENTIMENTO EM BACKGROUND (fire-and-forget) — ver "Armadilha" abaixo.
    /// </remarks>
    /// <remarks>
    /// ARMADILHA — análise de sentimento fire-and-forget (Task.Run não aguardado):
    /// - O bloco "_ = Task.Run(async () => { ... })" dispara a análise de sentimento em uma thread de
    ///   background e descarta a Task (atribuída a "_"). A resposta HTTP retorna IMEDIATAMENTE, sem
    ///   esperar o sentimento — por design, para não bloquear/atrasar a transcrição.
    /// - Consequências/riscos a conhecer:
    ///   * O resultado NÃO volta na resposta deste endpoint; ele é apenas logado (e/ou persistido pelo
    ///     serviço). Qualquer consumidor precisa obtê-lo por outro canal, não pela TranscricaoResponse.
    ///   * Exceções na Task NÃO propagam para o request: por isso TODO o corpo está dentro de try/catch
    ///     próprio que apenas faz LogWarning. Sem esse catch, uma exceção não observada poderia derrubar
    ///     o processo (comportamento de unobserved task exceptions).
    ///   * Não há await/CancellationToken: se o host reciclar/encerrar, a Task pode ser interrompida e o
    ///     sentimento se perde silenciosamente — aceitável aqui por ser dado complementar, não crítico.
    /// - Fallback de sentimento (Azure -> Python), nesta ordem:
    ///   1) Primário: se <c>_azureSentiment.IsConfigured</c> e a transcrição não for vazia/whitespace,
    ///      usa análise de sentimento TEXTUAL do Azure Language sobre o texto transcrito.
    ///   2) Fallback: se o Azure estiver inativo OU retornar null (falha/sem credenciais), cai para a
    ///      análise ACÚSTICA do serviço Python (<c>_mediaProcessor</c>), que processa o áudio bruto
    ///      (tom de voz/prosódia) em vez do texto. São abordagens diferentes para o mesmo objetivo.
    /// </remarks>
    /// <param name="dados">DTO de upload (multipart) contendo o arquivo de áudio/vídeo em <c>Arquivo</c>.</param>
    /// <returns>
    /// 200 OK com <see cref="TranscricaoResponse"/> (texto + provedor "azure"); 400 para arquivo
    /// ausente/vazio, formato inválido ou STT não configurado; 413 quando excede o limite de tamanho;
    /// 500 (Problem) em falha inesperada da transcrição.
    /// </returns>
    [HttpPost("transcrever")]
    public async Task<IActionResult> Transcrever([FromForm] UploadDto dados)
    {
        if (dados.Arquivo == null || dados.Arquivo.Length == 0)
            return BadRequest(new ErrorResponse("Nenhum arquivo."));

        // Whitelist de mimetype: áudio em geral + webm/mp4 (gravação de mic do navegador costuma vir como webm).
        if (!dados.Arquivo.ContentType.StartsWith("audio/") &&
            !dados.Arquivo.ContentType.StartsWith("video/webm") &&
            !dados.Arquivo.ContentType.StartsWith("video/mp4"))
            return BadRequest(new ErrorResponse("Formato inválido. Envie áudio ou vídeo (webm/mp4)."));

        // 413 (e não 400): pedido bem-formado, porém acima do limite configurado de bytes.
        if (dados.Arquivo.Length > _uploadLimits.MaxAudioUploadBytes)
            return StatusCode(413, new ErrorResponse("Arquivo de audio excede o limite configurado."));

        try
        {
            // Guard de configuração: sem provedor de STT (Azure) não há como transcrever -> 400 claro.
            if (!_transcricaoService.IsConfigured)
            {
                return BadRequest(new ErrorResponse("Nenhum serviço de transcrição (Azure-only) configurado."));
            }

            // Materializa o upload em bytes brutos (o STT e a análise acústica consomem byte[], não Stream).
            using var ms = new MemoryStream();
            await dados.Arquivo.CopyToAsync(ms);
            var audioBytes = ms.ToArray();
            var mimeType = dados.Arquivo.ContentType;

            _logger.LogInformation("Iniciando transcrição de áudio ({Size} MB)", audioBytes.Length / 1024.0 / 1024.0);

            // Header opcional: id da conexão SignalR do cliente, para o serviço empurrar progresso em tempo real.
            string? connectionId = Request.Headers["X-Connection-Id"];

            // 1. STT (Speech to Text) — etapa síncrona; é o resultado retornado ao cliente.
            var transcricao = await _transcricaoService.TranscreverAudio(audioBytes, mimeType, connectionId);

            // 2. Disparar análise de sentimento em background (FIRE-AND-FORGET — ver <remarks> "ARMADILHA").
            //    Estratégia: texto via Azure Language; se indisponível/falhar, fallback acústico via Python.
            //    A Task é descartada em "_": a resposta HTTP não espera por ela e exceções ficam contidas no
            //    try/catch interno (não propagam para o request), por isso só são logadas, nunca lançadas.
            _ = Task.Run(async () =>
            {
                try
                {
                    SentimentResult? sentiment = null;

                    // Caminho primário: análise TEXTUAL (Azure Language) sobre o texto transcrito,
                    // somente se o serviço estiver configurado e houver texto não-vazio para analisar.
                    if (_azureSentiment.IsConfigured && !string.IsNullOrWhiteSpace(transcricao))
                    {
                        sentiment = await _azureSentiment.AnalyzeSentimentAsync(transcricao);
                    }

                    // Fallback: Azure inativo OU retornou null -> análise ACÚSTICA (prosódia) via serviço Python,
                    // que opera sobre o áudio bruto em vez do texto.
                    if (sentiment == null)
                    {
                        _logger.LogWarning("Fallback: Analise Azure Sentiment falhou ou inativa. Usando analise acustica do Python.");
                        sentiment = await _mediaProcessor.AnalyzeSentimentAsync(audioBytes, mimeType);
                    }

                    // O resultado é apenas logado aqui (não retorna na resposta deste endpoint).
                    if (sentiment != null)
                    {
                        _logger.LogInformation("Analise de Sentimento Concluida: {Classification} - {Description}",
                            sentiment.Classification, sentiment.Description);
                    }
                }
                catch (Exception ex)
                {
                    // Contenção obrigatória: numa Task não-aguardada, exceção não tratada vira "unobserved"
                    // e pode derrubar o processo. Aqui rebaixamos para warning, pois o sentimento é complementar.
                    _logger.LogWarning("Erro na analise de sentimento em background: {Error}", ex.Message);
                }
            });

            // Retorna imediatamente: apenas a transcrição; o sentimento ainda pode estar rodando em background.
            return Ok(new TranscricaoResponse(transcricao, "azure"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na transcrição de áudio");
            return Problem("Falha na transcrição: " + ex.Message);
        }
    }

    /// <summary>
    /// Gera o laudo pericial textual a partir da transcrição de uma oitiva (via Azure OpenAI).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Valida que <c>dados.Transcricao</c> não é nula/vazia/whitespace; caso contrário, 400.
    /// 2. Guard de configuração: exige Azure OpenAI configurado (endpoint, deployment e chave);
    ///    se não estiver, 400 com instrução de configuração, evitando uma falha tardia.
    /// 3. Delega a <c>AnalisarTranscricaoOitiva</c>, que monta o prompt e chama o GPT-4o.
    ///    Defaults aplicados quando o cliente omite campos:
    ///      - <c>Duracao ?? "Não informada"</c>: string neutra para o prompt quando não há duração.
    ///      - <c>Contexto ?? ""</c>: contexto vazio (o serviço lida com ausência de contexto).
    /// 4. Retorna o laudo etiquetando o provedor como "azure-openai".
    ///
    /// Observação: este endpoint compartilha a mesma chamada de serviço de <see cref="AnalisarOitiva"/>;
    /// a diferença está na rota, no log e no rótulo de provedor da resposta.
    /// </remarks>
    /// <param name="dados">DTO da oitiva: <c>Transcricao</c> (obrigatória), <c>Duracao</c> e <c>Contexto</c> (opcionais).</param>
    /// <returns>200 OK com <see cref="AnaliseResponse"/> (laudo, provedor "azure-openai"); 400 se faltar transcrição ou IA não configurada; 500 (Problem) em falha.</returns>
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
            // Defaults: "Não informada"/"" evitam interpolar null no prompt do modelo.
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
    /// Analisa a transcrição de uma oitiva (mesma análise do laudo, exposta em rota própria e resposta "crua").
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Valida transcrição não-vazia (400 caso contrário).
    /// 2. Guard de configuração do serviço de descrição (Azure OpenAI); 400 se ausente.
    /// 3. Chama <c>AnalisarTranscricaoOitiva</c> com os mesmos defaults de <see cref="GerarLaudo"/>
    ///    (<c>Duracao ?? "Não informada"</c>, <c>Contexto ?? ""</c>).
    ///
    /// Diferenças em relação a <see cref="GerarLaudo"/>:
    /// - A resposta usa o construtor de <see cref="AnaliseResponse"/> SEM rótulo de provedor (provedor default).
    /// - O catch aqui NÃO faz LogError (apenas converte a exceção em Problem/500); use os logs do serviço
    ///   subjacente para diagnóstico neste endpoint.
    /// </remarks>
    /// <param name="dados">DTO da oitiva: <c>Transcricao</c> (obrigatória), <c>Duracao</c> e <c>Contexto</c> (opcionais).</param>
    /// <returns>200 OK com <see cref="AnaliseResponse"/>; 400 se faltar transcrição ou serviço não configurado; 500 (Problem) em falha.</returns>
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

            // Mesmos defaults de GerarLaudo para não interpolar null no prompt.
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
    /// Audita a conformidade da transcrição contra um roteiro/checklist esperado da oitiva (via Azure GPT-4o).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Valida transcrição não-vazia (400 caso contrário).
    /// 2. Guard de configuração do serviço de IA (Azure OpenAI); 400 se ausente.
    /// 3. Resolve o roteiro de referência:
    ///    - Se o cliente não enviar <c>Roteiro</c>, usa o roteiro PADRÃO da Opentech
    ///      "Padrao de Oitivas Opentech (Identificacao, Fatos, Conclusao)" — as três seções mínimas
    ///      que uma oitiva conforme deve cobrir. Esse default garante uma auditoria útil mesmo sem
    ///      o cliente especificar o checklist.
    /// 4. Chama <c>AuditarConformidade(transcricao, roteiro)</c>: o modelo compara o que foi dito na
    ///    oitiva com o roteiro e aponta lacunas/itens cumpridos.
    /// 5. Retorna o parecer com rótulo de provedor "azure-gpt4o".
    /// </remarks>
    /// <param name="dados">DTO de auditoria: <c>Transcricao</c> (obrigatória) e <c>Roteiro</c> (opcional; default Opentech).</param>
    /// <returns>200 OK com <see cref="AnaliseResponse"/> (parecer, provedor "azure-gpt4o"); 400 se faltar transcrição ou IA não configurada; 500 (Problem) em falha.</returns>
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
            // Default: na ausência de roteiro do cliente, audita contra o checklist mínimo da Opentech
            // (Identificacao, Fatos, Conclusao) — as seções que uma oitiva conforme deve conter.
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
    /// Extrai dados estruturados (objeto/JSON) a partir do texto livre da transcrição da oitiva.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Valida transcrição não-vazia (400 caso contrário).
    /// 2. Chama <c>ExtrairDadosOitiva</c>, que usa IA para transformar o texto corrido em campos
    ///    estruturados (ex.: identificação, datas, fatos relevantes) e retorna esse objeto.
    /// 3. Devolve o objeto extraído diretamente no corpo (serializado em JSON pelo ASP.NET Core).
    ///
    /// Particularidades deste endpoint:
    /// - NÃO checa <c>IsConfigured</c> antes de chamar: se o serviço não estiver configurado, a falha
    ///   surge dentro do try e é convertida em 500 (Problem), diferente dos demais endpoints que dão 400.
    /// - O catch não faz LogError; apenas mapeia a exceção para Problem/500.
    /// </remarks>
    /// <param name="dados">DTO da oitiva; apenas <c>Transcricao</c> (obrigatória) é usada aqui.</param>
    /// <returns>200 OK com o objeto de dados extraídos (JSON); 400 se faltar transcrição; 500 (Problem) em falha da extração.</returns>
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

