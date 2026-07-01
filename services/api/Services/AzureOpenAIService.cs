using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço (Azure-only) de integração com o Azure OpenAI (GPT-4o) para a análise forense de sinistros do Sentinel.
/// Encapsula a montagem do payload de chat completion e a chamada HTTP ao endpoint de deployment do Azure,
/// suportando três cenários: texto puro, imagem única e múltiplas imagens (ex.: keyframes extraídos de vídeo).
/// </summary>
/// <remarks>
/// COMO funciona (visão de módulo):
/// 1. A configuração (endpoint, deployment, chave e flag Enabled) vem de <see cref="AzureOpenAISettings"/>,
///    injetada via <see cref="IOptions{TOptions}"/>. Nada de credenciais/URLs hardcoded — este serviço é
///    exclusivamente Azure (não há mais caminho Gemini/Vertex/Central no projeto).
/// 2. Toda geração converge para <see cref="GenerateVisionAsync"/>, que é o único ponto que fala HTTP com o Azure.
///    <see cref="GenerateContentAsync"/> é apenas um atalho que adapta a assinatura "texto + 1 imagem" para a
///    lista de imagens que o método de visão espera.
/// 3. O serviço é registrado com um <see cref="HttpClient"/> tipado (HttpClientFactory), por isso o ciclo de vida
///    do socket é gerenciado fora desta classe; aqui apenas montamos e enviamos a requisição.
/// </remarks>
public class AzureOpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<AzureOpenAIService> _logger;

    /// <summary>
    /// Indica se o serviço pode ser usado: a integração precisa estar habilitada e ter uma chave de API.
    /// </summary>
    /// <remarks>
    /// COMO funciona: combina dois sinais de configuração — a flag explícita <c>Enabled</c> (liga/desliga a
    /// análise por IA sem remover credenciais) e a presença de <c>ApiKey</c> (sem chave não há como autenticar
    /// no Azure). Só retorna <c>true</c> quando ambos são verdadeiros. É consultado por <see cref="GenerateVisionAsync"/>
    /// antes de qualquer chamada de rede, servindo de guarda para falhar cedo com mensagem clara.
    /// </remarks>
    public bool IsConfigured => _settings.Enabled && !string.IsNullOrEmpty(_settings.ApiKey);

    /// <summary>
    /// Constrói o serviço com as dependências injetadas pelo container de DI.
    /// </summary>
    /// <param name="httpClient">Cliente HTTP tipado (gerenciado pelo HttpClientFactory) usado para falar com o endpoint do Azure.</param>
    /// <param name="settings">Configurações do Azure OpenAI (endpoint, deployment, chave, flag Enabled). Note o uso de <c>settings.Value</c> para materializar o snapshot de opções.</param>
    /// <param name="logger">Logger usado para rastrear envio de requisições e registrar erros de API.</param>
    public AzureOpenAIService(
        HttpClient httpClient,
        IOptions<AzureOpenAISettings> settings,
        ILogger<AzureOpenAIService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gera conteúdo usando GPT-4o no cenário simples de texto puro ou uma única imagem.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. É uma fachada fina sobre <see cref="GenerateVisionAsync"/> — não fala HTTP diretamente.
    /// 2. Normaliza a entrada "0 ou 1 imagem" para a <c>List</c> de tuplas (Base64, MimeType) que o método de visão consome:
    ///    se <paramref name="base64Image"/> vier nulo/vazio, passa uma lista vazia (chamada só-texto); caso contrário,
    ///    embrulha a imagem única em uma lista de um item.
    /// 3. Delega para <see cref="GenerateVisionAsync"/>, herdando dele o limite padrão de tokens e o comportamento de
    ///    temperatura/parse. Por isso existe como conveniência para os chamadores que não precisam de múltiplas imagens nem legendas.
    /// </remarks>
    /// <param name="prompt">Instrução/pergunta do usuário enviada como conteúdo da mensagem "user".</param>
    /// <param name="systemPrompt">Prompt de sistema (papel/diretrizes do perito). Se vazio, nenhuma mensagem "system" é adicionada.</param>
    /// <param name="base64Image">Imagem opcional em Base64 (sem o prefixo data URI). Nulo/vazio significa requisição apenas de texto.</param>
    /// <param name="mimeType">MIME type da imagem (padrão "image/jpeg"); usado para montar o data URI quando há imagem.</param>
    /// <returns>O texto gerado pelo modelo (conteúdo de <c>choices[0].message.content</c>), ou "Sem resposta." se vier nulo.</returns>
    /// <exception cref="InvalidOperationException">Propagada de <see cref="GenerateVisionAsync"/> quando o serviço não está configurado.</exception>
    /// <exception cref="HttpRequestException">Propagada de <see cref="GenerateVisionAsync"/> quando o Azure retorna status de erro.</exception>
    public async Task<string> GenerateContentAsync(string prompt, string systemPrompt = "", string? base64Image = null, string mimeType = "image/jpeg")
    {
        var images = string.IsNullOrEmpty(base64Image)
            ? new List<(string Base64, string MimeType)>()
            : new List<(string Base64, string MimeType)> { (base64Image, mimeType) };

        return await GenerateVisionAsync(prompt, systemPrompt, images);
    }

    /// <summary>
    /// Núcleo da integração: monta o payload de chat completion e chama o Azure OpenAI (GPT-4o),
    /// suportando texto e zero ou mais imagens (ex.: keyframes extraídos de vídeo).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. GUARDA: valida <see cref="IsConfigured"/> e lança se a integração estiver desligada ou sem chave —
    ///    falha cedo antes de qualquer custo de rede.
    /// 2. URL: monta o endpoint do Azure no formato
    ///    <c>{Endpoint}openai/deployments/{DeploymentName}/chat/completions?api-version=2024-02-15-preview</c>.
    ///    O <c>api-version=2024-02-15-preview</c> é fixado de propósito: é a versão da API do Azure OpenAI que
    ///    garante o suporte ao conteúdo multimodal (image_url) usado aqui; subir/baixar essa versão pode mudar o
    ///    contrato do payload, por isso ela é um número "mágico" intencional e deve ser alterada com cautela.
    /// 3. MENSAGEM "system" (opcional): só é adicionada quando há <paramref name="systemPrompt"/> não vazio.
    ///    É onde entram as diretrizes do perito/laudo.
    /// 4. MENSAGEM "user":
    ///    - COM imagens: o conteúdo vira um array multimodal. Começa com o bloco de texto do <paramref name="prompt"/>
    ///      e, para cada imagem, INTERCALA opcionalmente a legenda correspondente (ex.: timestamp do quadro) ANTES da
    ///      imagem. Essa ordem (legenda → imagem) é deliberada: ancora cada quadro no tempo para o modelo correlacionar
    ///      o que vê com o instante do vídeo. A imagem é embutida como data URI Base64 (<c>data:{mime};base64,{...}</c>);
    ///      se o MIME vier vazio, assume "image/jpeg" como padrão seguro.
    ///    - SEM imagens: o conteúdo é apenas a string do prompt (chamada só-texto).
    /// 5. PARÂMETROS de amostragem: <c>temperature = 0.0</c> e <c>top_p = 1</c> tornam a saída praticamente determinística —
    ///    escolha proposital para laudos periciais, onde se quer precisão e reprodutibilidade, não criatividade.
    ///    <paramref name="maxTokens"/> limita o tamanho da resposta (padrão 8192 tokens, suficiente para laudos longos).
    /// 6. AUTENTICAÇÃO: a chave vai no header <c>api-key</c> (padrão do Azure OpenAI, diferente do <c>Authorization: Bearer</c>
    ///    da OpenAI pública). O corpo é JSON UTF-8.
    /// 7. ENVIO + TRATAMENTO DE ERRO: em status não-sucesso, loga corpo/status e lança <see cref="HttpRequestException"/>.
    /// 8. PARSE: extrai o texto de <c>choices[0].message.content</c> (primeira/única escolha). Se o campo vier nulo,
    ///    retorna o fallback "Sem resposta." para nunca devolver <c>null</c> ao chamador.
    /// </remarks>
    /// <param name="prompt">Texto principal da mensagem "user" (a instrução de análise).</param>
    /// <param name="systemPrompt">Prompt de sistema; se vazio, a mensagem "system" é omitida.</param>
    /// <param name="images">Lista de imagens (Base64 sem prefixo, MIME). Vazia = chamada apenas de texto.</param>
    /// <param name="maxTokens">Teto de tokens da resposta. Padrão 8192, dimensionado para laudos extensos.</param>
    /// <param name="captions">Legendas opcionais alinhadas por índice às imagens (ex.: timestamps). Cada legenda não vazia é inserida antes da imagem de mesmo índice.</param>
    /// <returns>O conteúdo textual de <c>choices[0].message.content</c>, ou "Sem resposta." se nulo.</returns>
    /// <exception cref="InvalidOperationException">Lançada quando <see cref="IsConfigured"/> é falso (integração desabilitada ou sem chave).</exception>
    /// <exception cref="HttpRequestException">Lançada quando o Azure OpenAI responde com status de erro (não-2xx).</exception>
    public async Task<string> GenerateVisionAsync(
        string prompt,
        string systemPrompt,
        List<(string Base64, string MimeType)> images,
        int maxTokens = 8192,
        List<string>? captions = null)
    {
        // Guarda: sem configuração válida não há como autenticar/chamar o Azure — falha cedo e explícita.
        if (!IsConfigured)
            throw new InvalidOperationException("Azure OpenAI não está configurado.");

        // api-version fixa em 2024-02-15-preview: versão que suporta o conteúdo multimodal (image_url) usado abaixo.
        var url = $"{_settings.Endpoint}openai/deployments/{_settings.DeploymentName}/chat/completions?api-version=2024-02-15-preview";

        var messages = new List<object>();

        // Mensagem "system" só entra quando há diretrizes a passar (papel do perito, formato do laudo, etc.).
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        if (images.Count > 0)
        {
            // Conteúdo multimodal: primeiro o texto do prompt, depois os pares (legenda?) + imagem.
            var content = new List<object> { new { type = "text", text = prompt } };
            for (int i = 0; i < images.Count; i++)
            {
                // Legenda alinhada por índice (ex.: timestamp do keyframe) inserida ANTES da imagem para ancorar o quadro no tempo.
                if (captions != null && i < captions.Count && !string.IsNullOrEmpty(captions[i]))
                {
                    content.Add(new { type = "text", text = captions[i] });
                }

                var (base64, mime) = images[i];
                // MIME ausente cai para "image/jpeg" — default seguro para keyframes/fotos sem metadado de tipo.
                var mimeReal = string.IsNullOrEmpty(mime) ? "image/jpeg" : mime;
                // Imagem embutida como data URI Base64 (inline), evitando hospedar/expor URLs externas.
                content.Add(new { type = "image_url", image_url = new { url = $"data:{mimeReal};base64,{base64}" } });
            }
            messages.Add(new { role = "user", content = content.ToArray() });
        }
        else
        {
            // Sem imagens: payload de texto puro.
            messages.Add(new { role = "user", content = prompt });
        }

        var payload = new
        {
            messages = messages,
            max_tokens = maxTokens,
            temperature = 0.0, // Saída determinística: alta precisão/reprodutibilidade exigida em laudos periciais
            top_p = 1          // Sem truncamento de núcleo; combinado com temperature=0 reforça a determinismo
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        // Azure OpenAI autentica via header "api-key" (e não via Authorization: Bearer da OpenAI pública).
        request.Headers.Add("api-key", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Enviando requisição para Azure OpenAI: {Deployment} ({ImageCount} imagem/imagens)",
            _settings.DeploymentName, images.Count);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // Loga corpo completo (útil para diagnosticar rejeições de conteúdo/limites) e converte em exceção HTTP.
            _logger.LogError("Erro Azure OpenAI ({Status}): {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Azure OpenAI error: {response.StatusCode}");
        }

        // Parse mínimo do envelope de resposta: pega o texto da primeira escolha; fallback evita retornar null.
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString() ?? "Sem resposta.";
    }
}
