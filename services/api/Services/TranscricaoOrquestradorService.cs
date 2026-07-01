using Microsoft.AspNetCore.SignalR;
using SinistroAPI.Hubs;
using SinistroAPI.Interfaces;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

/// <summary>
/// Orquestra a transcricao de audio das oitivas em modo Azure-only, com fallback entre provedores.
/// </summary>
/// <remarks>
/// COMO funciona:
/// Esta classe e o ponto unico de entrada para transformar bytes de audio em texto transcrito.
/// Ela coordena tres servicos e reporta progresso ao front-end via SignalR:
///   1. <see cref="MediaProcessorService"/> (opcional): pre-processa/comprime o audio quando vale a pena.
///   2. <see cref="AzureFastTranscricaoService"/> (Azure Speech-to-Text / Fast transcription): provedor PRIMARIO,
///      pois oferece diarizacao (separacao de falantes), util para oitivas com entrevistador x entrevistado.
///   3. <see cref="AzureWhisperService"/> (Azure OpenAI Whisper): FALLBACK acionado quando o Speech-to-Text
///      falha ou devolve uma transcricao de baixa qualidade.
/// O design e "Azure-only": nao ha provedores externos (ex.: Gemini/Vertex foram removidos). Se nenhum
/// provedor Azure estiver configurado ou todos falharem, a transcricao e considerada indisponivel.
/// </remarks>
public class TranscricaoOrquestradorService : ITranscricaoService
{
    private readonly AzureFastTranscricaoService _azureSpeechToText;
    private readonly AzureWhisperService _azureWhisper;
    private readonly MediaProcessorService _mediaProcessor;
    private readonly ILogger<TranscricaoOrquestradorService> _logger;
    private readonly IHubContext<AnalysisHub> _hubContext;

    /// <summary>
    /// Indica se ha ao menos um provedor de transcricao Azure configurado e utilizavel.
    /// </summary>
    /// <remarks>
    /// COMO funciona: e um OR logico entre os dois provedores. Basta que Speech-to-Text OU Whisper
    /// esteja configurado para que o orquestrador consiga produzir alguma transcricao. Usado a montante
    /// para decidir se a funcionalidade de transcricao deve sequer ser oferecida.
    /// </remarks>
    public bool IsConfigured => _azureSpeechToText.IsConfigured || _azureWhisper.IsConfigured;

    /// <summary>
    /// Injeta os provedores Azure, o pre-processador de midia, o logger e o hub SignalR de progresso.
    /// </summary>
    /// <param name="azureSpeechToText">Provedor primario (Azure Speech-to-Text com diarizacao).</param>
    /// <param name="azureWhisper">Provedor de fallback (Azure OpenAI Whisper).</param>
    /// <param name="mediaProcessor">Servico de pre-processamento/compressao de audio (pode estar desabilitado).</param>
    /// <param name="logger">Logger para rastrear o caminho de fallback e falhas.</param>
    /// <param name="hubContext">Hub SignalR usado para enviar atualizacoes de progresso ao cliente.</param>
    public TranscricaoOrquestradorService(
        AzureFastTranscricaoService azureSpeechToText,
        AzureWhisperService azureWhisper,
        MediaProcessorService mediaProcessor,
        ILogger<TranscricaoOrquestradorService> logger,
        IHubContext<AnalysisHub> hubContext)
    {
        _azureSpeechToText = azureSpeechToText;
        _azureWhisper = azureWhisper;
        _mediaProcessor = mediaProcessor;
        _logger = logger;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Transcreve um audio para texto usando a cadeia Azure Speech-to-Text -> Whisper, com pre-processamento opcional.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    ///   1. PRE-PROCESSAMENTO (opcional): chama <see cref="DevePreProcessarAudio"/> para decidir se vale comprimir
    ///      o audio. So pre-processa quando o pre-processador esta habilitado E o heuristico aprova. Se a compressao
    ///      tiver sucesso, troca os bytes pelo audio otimizado e ajusta o mimeType para "audio/mpeg" (MP3). Falhas de
    ///      pre-processamento sao apenas logadas (warning) e o fluxo segue com o audio ORIGINAL — nunca interrompem a
    ///      transcricao. Quando o pre-processamento e ignorado por heuristica, isso e logado para preservar a qualidade.
    ///   2. PROVEDOR PRIMARIO (Azure Speech-to-Text): se configurado, tenta primeiro por oferecer diarizacao. O
    ///      resultado passa por <see cref="TranscricaoPareceValida"/>; se for considerado bom, retorna imediatamente.
    ///      Se vier fraco/repetitivo OU lancar excecao, registra o motivo em <c>ultimaFalha</c> e cai para o fallback.
    ///   3. FALLBACK (Azure Whisper): se configurado, tenta transcrever. O resultado do Whisper e retornado SEM passar
    ///      pelo filtro de qualidade (e o ultimo recurso; qualquer texto e melhor que nenhum).
    ///   4. INDISPONIVEL: se nenhum provedor produzir texto, lanca <see cref="InvalidOperationException"/> incluindo a
    ///      ultima falha conhecida para diagnostico.
    /// PROGRESSO (SignalR): os percentuais (5/10/35/70/100) sao marcos fixos do pipeline — 5 = inicio do
    /// pre-processamento, 10 = inicio da orquestracao, 35 = tentativa Speech-to-Text, 70 = tentativa Whisper,
    /// 100 = conclusao (sucesso ou esgotamento). Sao enviados via <see cref="SendProgressAsync"/> e so chegam ao
    /// cliente quando ha <paramref name="connectionId"/>.
    /// </remarks>
    /// <param name="audioBytes">Bytes brutos do audio a transcrever. Pode ser substituido internamente pelo audio comprimido.</param>
    /// <param name="mimeType">MIME type do audio (ex.: "audio/wav"); pode ser ajustado para "audio/mpeg" apos compressao.</param>
    /// <param name="connectionId">Conexao SignalR opcional para receber o progresso; quando nulo/vazio, o progresso e silenciosamente ignorado.</param>
    /// <returns>O texto transcrito (ja com <c>Trim</c> aplicado) do primeiro provedor que tiver sucesso.</returns>
    /// <exception cref="InvalidOperationException">Lancada quando nenhum provedor Azure esta disponivel ou todos falham.</exception>
    public async Task<string> TranscreverAudio(byte[] audioBytes, string mimeType, string? connectionId = null)
    {
        // Pre-processamento so quando realmente necessario.
        // Em audios longos comprimidos, preservar o original evita perda de inteligibilidade.
        var devePreProcessar = DevePreProcessarAudio(audioBytes.Length, mimeType);

        if (_mediaProcessor.IsEnabled && devePreProcessar)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 5);
                var processed = await _mediaProcessor.ProcessAudioAsync(audioBytes, mimeType);
                if (processed?.ProcessedBytes is { Length: > 0 })
                {
                    _logger.LogInformation(
                        "Audio otimizado: {OriginalKb} KB -> {ProcessedKb} KB",
                        audioBytes.Length / 1024,
                        processed.ProcessedBytes.Length / 1024);

                    audioBytes = processed.ProcessedBytes;
                    mimeType = "audio/mpeg";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Falha no pre-processamento, seguindo com audio original: {Error}", ex.Message);
            }
        }
        else if (_mediaProcessor.IsEnabled)
        {
            _logger.LogInformation(
                "Pre-processamento ignorado para preservar qualidade (Mime={MimeType}, SizeKb={SizeKb})",
                mimeType,
                audioBytes.Length / 1024);
        }

        _logger.LogInformation("Orquestrador: Iniciando transcricao Azure-only (Speech-to-Text -> Whisper)");
        await SendProgressAsync(connectionId, "", 10);

        string? ultimaFalha = null;

        // 1) Prioridade: Azure Speech-to-Text (Fast transcription + diarizacao).
        if (_azureSpeechToText.IsConfigured)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 35);
                _logger.LogInformation("Tentando Azure Speech-to-Text...");

                var resultadoStt = (await _azureSpeechToText.TranscreverAsync(audioBytes, mimeType) ?? string.Empty).Trim();
                if (TranscricaoPareceValida(resultadoStt))
                {
                    await SendProgressAsync(connectionId, "", 100);
                    return resultadoStt;
                }

                ultimaFalha = "Resultado do Azure Speech-to-Text com baixa densidade/qualidade.";
                _logger.LogWarning("Azure Speech-to-Text retornou transcricao fraca; tentando Whisper.");
            }
            catch (Exception ex)
            {
                ultimaFalha = ex.Message;
                _logger.LogWarning("Falha no Azure Speech-to-Text: {Error}", ex.Message);
            }
        }

        // 2) Fallback Azure-only: Whisper.
        if (_azureWhisper.IsConfigured)
        {
            try
            {
                await SendProgressAsync(connectionId, "", 70);
                _logger.LogInformation("Tentando Azure Whisper...");

                var resultadoWhisper = (await _azureWhisper.TranscreverAsync(audioBytes, mimeType) ?? string.Empty).Trim();
                await SendProgressAsync(connectionId, "", 100);
                return resultadoWhisper;
            }
            catch (Exception ex)
            {
                ultimaFalha = ex.Message;
                _logger.LogError("Falha no Azure Whisper: {Error}", ex.Message);
            }
        }

        await SendProgressAsync(connectionId, "", 100);
        throw new InvalidOperationException(
            $"Transcricao indisponivel em modo Azure-only. Ultimo erro: {ultimaFalha ?? "desconhecido"}");
    }

    /// <summary>
    /// Extrairia campos estruturados da oitiva a partir da transcricao — atualmente desativada (no-op).
    /// </summary>
    /// <remarks>
    /// COMO funciona: no modo Azure-only este passo esta intencionalmente desligado e sempre devolve um
    /// dicionario vazio (a extracao estruturada dependia de um provedor de IA que foi removido). O metodo e
    /// mantido para satisfazer o contrato <see cref="ITranscricaoService"/> e nao quebrar chamadores existentes.
    /// Apenas registra um log informativo; nao realiza nenhuma chamada externa nem processamento.
    /// </remarks>
    /// <param name="transcricao">Transcricao de origem (ignorada na implementacao atual).</param>
    /// <returns>Sempre um <see cref="Dictionary{TKey,TValue}"/> vazio, ja concluido.</returns>
    public Task<Dictionary<string, string>> ExtrairDadosOitiva(string transcricao)
    {
        _logger.LogInformation("Extracao de dados desativada em modo Azure-only.");
        return Task.FromResult(new Dictionary<string, string>());
    }

    /// <summary>
    /// Envia uma atualizacao de progresso ao cliente SignalR identificado, de forma resiliente a falhas.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    ///   1. Se nao houver <paramref name="connectionId"/> (chamada sem cliente conectado), retorna sem fazer nada —
    ///      isso permite chamar a transcricao tanto via UI (com progresso) quanto em background (sem progresso).
    ///   2. Invoca o metodo de cliente "ReceiveProgress" no hub <see cref="AnalysisHub"/>, passando a mensagem e o percentual.
    ///   3. Qualquer excecao no envio e DELIBERADAMENTE engolida (catch vazio): uma falha de notificacao de progresso
    ///      (ex.: cliente desconectou) jamais deve interromper a transcricao em andamento, que e a operacao critica.
    /// </remarks>
    /// <param name="connectionId">Id da conexao SignalR alvo; nulo/vazio faz o metodo nao operar.</param>
    /// <param name="message">Mensagem textual de status associada ao percentual.</param>
    /// <param name="percent">Percentual de progresso (0-100) correspondente ao marco atual do pipeline.</param>
    private async Task SendProgressAsync(string? connectionId, string message, int percent)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        try
        {
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", message, percent);
        }
        catch
        {
            // Nao quebra o fluxo de transcricao por falha de progresso.
        }
    }

    /// <summary>
    /// Heuristica que decide se o audio deve passar pelo pre-processamento (compressao) antes de transcrever.
    /// </summary>
    /// <remarks>
    /// COMO funciona e PORQUE dos limiares:
    ///   1. LIMITE DE TAMANHO: se o audio tem 35 MB ou mais (35 * 1024 * 1024 bytes), retorna <c>true</c>. Arquivos
    ///      grandes tendem a estourar limites de upload/tamanho dos provedores de transcricao e se beneficiam da
    ///      reducao; o ganho de reduzir tamanho compensa a (pequena) perda de qualidade. 35 MB e a margem escolhida
    ///      para ficar abaixo dos limites tipicos de API mantendo audios comuns intactos.
    ///   2. FORMATOS NAO COMPRIMIDOS: independentemente do tamanho, formatos WAV/PCM brutos (audio/wav, audio/x-wav,
    ///      audio/wave, audio/vnd.wave, audio/pcm, audio/l16) sao sempre pre-processados, pois sao nao comprimidos e
    ///      desperdicam banda/quota — converter para MP3 reduz drasticamente o tamanho sem perda perceptivel para fala.
    ///   3. Qualquer outro caso (formatos ja comprimidos e abaixo do limite, ex.: MP3/M4A pequenos) retorna <c>false</c>
    ///      para PRESERVAR a inteligibilidade do audio original (recomprimir audio ja comprimido degrada a transcricao).
    /// O mimeType e normalizado (trim + minusculas) e tratado como vazio quando nulo, para comparacao robusta.
    /// </remarks>
    /// <param name="audioBytesLength">Tamanho do audio em bytes.</param>
    /// <param name="mimeType">MIME type declarado do audio.</param>
    /// <returns><c>true</c> se o audio deve ser pre-processado; caso contrario, <c>false</c>.</returns>
    private static bool DevePreProcessarAudio(int audioBytesLength, string mimeType)
    {
        // Audio muito grande tende a se beneficiar da reducao de tamanho.
        if (audioBytesLength >= 35 * 1024 * 1024)
        {
            return true;
        }

        var mime = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        return mime is "audio/wav"
            or "audio/x-wav"
            or "audio/wave"
            or "audio/vnd.wave"
            or "audio/pcm"
            or "audio/l16";
    }

    /// <summary>
    /// Avalia heuristicamente se uma transcricao tem qualidade suficiente para ser aceita (vs. cair no fallback).
    /// </summary>
    /// <remarks>
    /// COMO funciona (filtro de qualidade passo a passo) e PORQUE dos numeros:
    ///   1. VAZIO: transcricao nula/branca e invalida (false).
    ///   2. CURTA DEMAIS: divide em linhas nao vazias (com trim). Se houver no maximo 2 linhas E o texto total tiver
    ///      menos de 180 caracteres, considera invalida. O par "ate 2 linhas + menos de 180 chars" filtra resultados
    ///      quase vazios (ex.: ruido, uma unica palavra) sem descartar respostas curtas porem legitimas mais densas.
    ///   3. NORMALIZACAO: para cada linha, remove o prefixo de metadados de diarizacao no formato "[mm:ss] Falante: "
    ///      (regex <c>^\[\d{2}:\d{2}\]\s*[^:]+:\s*</c>), passa para minusculas e colapsa espacos. Isso isola o
    ///      CONTEUDO falado, ignorando timestamps/rotulos de falante que variam mesmo quando a fala se repete.
    ///   4. SEM CONTEUDO: se nenhuma frase sobrar apos a normalizacao, e invalida.
    ///   5. DETECCAO DE LOOP/ALUCINACAO: agrupa as frases iguais e encontra a "dominante" (a que mais se repete).
    ///      Calcula a fracao <c>repeticaoDominante = ocorrencias_da_dominante / total_de_frases</c>. Se houver pelo
    ///      menos 8 frases (piso para a estatistica ser significativa e nao penalizar transcricoes curtas) E a
    ///      dominante representar 70% ou mais do total (>= 0.70), considera invalida — esse padrao indica que o
    ///      provedor entrou em loop/alucinacao repetindo a mesma frase, sintoma classico de transcricao degenerada.
    ///   6. Caso contrario, a transcricao e aceita (true).
    /// O piso de 8 frases evita falsos positivos: em respostas curtas e natural que uma frase domine sem ser erro.
    /// </remarks>
    /// <param name="transcricao">Texto transcrito a ser avaliado (potencialmente com prefixos de diarizacao por linha).</param>
    /// <returns><c>true</c> se a transcricao parece valida; <c>false</c> se deve ser descartada em favor do fallback.</returns>
    private static bool TranscricaoPareceValida(string transcricao)
    {
        if (string.IsNullOrWhiteSpace(transcricao))
        {
            return false;
        }

        var linhas = transcricao
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (linhas.Count <= 2 && transcricao.Length < 180)
        {
            return false;
        }

        var frases = new List<string>(linhas.Count);
        foreach (var linha in linhas)
        {
            var semMeta = Regex.Replace(linha, @"^\[\d{2}:\d{2}\]\s*[^:]+:\s*", string.Empty);
            var norm = Regex.Replace(semMeta.ToLowerInvariant(), @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(norm))
            {
                frases.Add(norm);
            }
        }

        if (frases.Count == 0)
        {
            return false;
        }

        var dominante = frases
            .GroupBy(f => f)
            .Select(g => new { Texto = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .First();

        var repeticaoDominante = (double)dominante.Count / frases.Count;
        if (frases.Count >= 8 && repeticaoDominante >= 0.70)
        {
            return false;
        }

        return true;
    }
}
