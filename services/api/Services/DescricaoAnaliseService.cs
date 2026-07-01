using SinistroAPI.Interfaces;
using System.Text;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de DESCRIÇÕES textuais de sinistros (módulo do sistema Sentinel).
/// Útil para analisar boletins de ocorrência, relatos escritos, transcrições de oitiva, etc. Utiliza Azure OpenAI (GPT-4o).
/// </summary>
/// <remarks>
/// COMO funciona (visão geral do módulo):
/// 1. Todos os métodos públicos validam <see cref="IsConfigured"/> antes de qualquer chamada externa e
///    lançam <see cref="InvalidOperationException"/> se o Azure OpenAI não estiver configurado (fail-fast).
/// 2. Cada operação monta um PROMPT específico (região "Prompts") e o envia ao Azure OpenAI via
///    <see cref="AzureOpenAIService.GenerateContentAsync"/>, sempre acompanhado de um SYSTEM PROMPT que define
///    o papel do modelo (perito forense ou auditor) e a regra anti-alucinação (exigência de citar a fonte).
/// 3. O texto livre retornado pelo modelo passa, quando aplicável, por pós-processamento determinístico
///    (<see cref="PosProcessarTranscricao"/>) que padroniza a nomenclatura dos interlocutores.
///
/// Arquitetura: Azure-only. Não há integração com Gemini/Vertex/Central — qualquer resíduo desses termos
/// no texto gerado é normalizado pelo pós-processamento. A persona usada nos prompts é "Perito Forense da Opentech".
/// </remarks>
public class DescricaoAnaliseService : IDescricaoAnaliseService
{
    private readonly ILogger<DescricaoAnaliseService> _logger;
    private readonly AzureOpenAIService _azureOpenAI;

    /// <summary>
    /// Injeta as dependências do serviço via DI.
    /// </summary>
    /// <param name="logger">Logger para rastrear o início de cada análise (telemetria/diagnóstico).</param>
    /// <param name="azureOpenAI">Cliente que encapsula a chamada ao Azure OpenAI (GPT-4o) e expõe o estado de configuração.</param>
    public DescricaoAnaliseService(ILogger<DescricaoAnaliseService> logger, AzureOpenAIService azureOpenAI)
    {
        _logger = logger;
        _azureOpenAI = azureOpenAI;
    }

    /// <summary>
    /// Indica se o serviço está pronto para uso (delega ao estado de configuração do Azure OpenAI).
    /// </summary>
    /// <remarks>
    /// COMO funciona: apenas repassa <see cref="AzureOpenAIService.IsConfigured"/>. Serve como guarda
    /// (fail-fast) consultado no início de todos os métodos públicos para evitar chamadas remotas sem credenciais.
    /// </remarks>
    public bool IsConfigured => _azureOpenAI.IsConfigured;

    /// <summary>
    /// Analisa uma descrição textual de sinistro (boletim, relato escrito) e devolve um laudo estruturado.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline):
    /// 1. Valida <see cref="IsConfigured"/>; se falso, lança <see cref="InvalidOperationException"/> (fail-fast).
    /// 2. Monta o prompt de extração via <see cref="GetPromptAnaliseDescricao"/> (tabela de dados + citações da fonte).
    /// 3. Concatena o <paramref name="contextoUsuario"/> sob o rótulo "CONTEXTO ADICIONAL" para enriquecer a análise
    ///    sem alterar a estrutura do laudo.
    /// 4. Envia ao Azure OpenAI com o system prompt de perito forense (<see cref="GetSystemPrompt"/>), que impõe
    ///    tom técnico/formal e a regra anti-alucinação (citar trecho ou escrever "Não mencionado").
    /// Diferente de <see cref="AnalisarTranscricaoOitiva"/>, aqui NÃO há pós-processamento de nomenclatura,
    /// pois documentos escritos não possuem o padrão de interlocutores Operador/Motorista.
    /// </remarks>
    /// <param name="descricao">Texto bruto do documento a analisar (boletim de ocorrência, relato, etc.).</param>
    /// <param name="tipoDocumento">Rótulo do tipo de documento exibido no prompt (ex.: "Relato"); apenas contextualiza o laudo.</param>
    /// <param name="contextoUsuario">Informação extra opcional anexada ao prompt; vazio quando não há contexto.</param>
    /// <returns>Laudo de análise em Markdown gerado pelo modelo.</returns>
    /// <exception cref="InvalidOperationException">Lançada quando o Azure OpenAI não está configurado.</exception>
    public async Task<string> AnalisarDescricao(string descricao, string tipoDocumento = "Relato", string contextoUsuario = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure OpenAI nao esta configurado.");
        }

        _logger.LogInformation("Iniciando analise de descricao ({TipoDocumento})", tipoDocumento);

        var prompt = GetPromptAnaliseDescricao(descricao, tipoDocumento);
        var fullPrompt = $"{prompt}\n\nCONTEXTO ADICIONAL: {contextoUsuario}";

        return await _azureOpenAI.GenerateContentAsync(fullPrompt, GetSystemPrompt());
    }

    /// <summary>
    /// Analisa a transcrição de uma oitiva (ligação Operador BAS x Motorista) e produz o laudo pericial padronizado.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline):
    /// 1. Valida <see cref="IsConfigured"/>; se falso, lança <see cref="InvalidOperationException"/> (fail-fast).
    /// 2. Monta o prompt "Detetive" via <see cref="GetPromptAnaliseOitiva"/>, que injeta a <paramref name="duracao"/>,
    ///    o <paramref name="tipoOperacao"/> e exige uma tabela de DADOS IDENTIFICADOS seguida do separador
    ///    "---SEPARADOR_DADOS---" (consumido depois pela camada de UI para dividir cabeçalho e corpo do laudo).
    /// 3. Reforça o contexto do usuário com <see cref="GetContextoReforcadoAntiAlucinacao"/> e o anexa como
    ///    "CONTEXTO ADICIONAL".
    /// 4. Envia ao Azure OpenAI com o system prompt de perito forense (<see cref="GetSystemPrompt"/>).
    /// 5. PÓS-PROCESSA o resultado com <see cref="PosProcessarTranscricao"/> para forçar a nomenclatura canônica
    ///    dos interlocutores (Operador BAS / Motorista), corrigir variações que o modelo possa emitir e remover
    ///    o termo legado "Central". Esse passo é determinístico (substituição de strings), não usa IA.
    /// </remarks>
    /// <param name="transcricao">Texto da transcrição da ligação a ser periciada.</param>
    /// <param name="duracao">Duração da ligação (string já formatada) injetada no prompt para contextualizar a análise temporal.</param>
    /// <param name="contextoUsuario">Contexto adicional opcional; quando vazio, é substituído por um marcador neutro.</param>
    /// <param name="tipoOperacao">Tipo de operação (ex.: "Viagem") usado pelo prompt para ajustar o enquadramento do laudo.</param>
    /// <returns>Laudo pericial em Markdown, já com a nomenclatura de interlocutores normalizada.</returns>
    /// <exception cref="InvalidOperationException">Lançada quando o Azure OpenAI não está configurado.</exception>
    public async Task<string> AnalisarTranscricaoOitiva(string transcricao, string duracao, string contextoUsuario = "", string tipoOperacao = "Viagem")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure OpenAI nao esta configurado.");
        }

        _logger.LogInformation("Iniciando analise de transcricao de oitiva");

        var prompt = GetPromptAnaliseOitiva(transcricao, duracao, tipoOperacao);
        var contextoReforcado = GetContextoReforcadoAntiAlucinacao(contextoUsuario);
        var fullPrompt = $"{prompt}\n\nCONTEXTO ADICIONAL: {contextoReforcado}";

        var resultado = await _azureOpenAI.GenerateContentAsync(fullPrompt, GetSystemPrompt());
        return PosProcessarTranscricao(resultado);
    }

    /// <summary>
    /// Audita a conformidade de uma conversa (Operador x Motorista) contra um roteiro de referência.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline):
    /// 1. Valida <see cref="IsConfigured"/>; se falso, lança <see cref="InvalidOperationException"/> (fail-fast).
    /// 2. Monta o prompt de auditoria via <see cref="GetPromptAuditoriaConformidade"/>, que coloca o
    ///    <paramref name="roteiroConformidade"/> como REFERÊNCIA e a <paramref name="transcricao"/> como objeto da
    ///    auditoria, e instrui o modelo a verificar cada ponto, classificar o tom de voz e atribuir um SCORE
    ///    de confiança de 0 a 100% (limiar percentual para quantificar aderência ao roteiro).
    /// 3. Envia ao Azure OpenAI com um system prompt DEDICADO de auditor (<see cref="GetSystemPromptAuditoria"/>),
    ///    distinto do perito forense, com postura crítica/detalhista focada em apontar não-conformidades.
    /// Diferente da oitiva, NÃO há pós-processamento de nomenclatura — o laudo de auditoria não exige o padrão
    /// fixo de interlocutores e é retornado exatamente como o modelo produziu.
    /// </remarks>
    /// <param name="transcricao">Transcrição da conversa a ser auditada.</param>
    /// <param name="roteiroConformidade">Roteiro/checklist esperado usado como referência para apontar desvios.</param>
    /// <returns>Relatório (laudo) de auditoria em Markdown com não-conformidades, tom de voz e score de confiança.</returns>
    /// <exception cref="InvalidOperationException">Lançada quando o Azure OpenAI não está configurado.</exception>
    public async Task<string> AuditarConformidade(string transcricao, string roteiroConformidade)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure OpenAI nao esta configurado.");
        }

        _logger.LogInformation("Iniciando AUDITORIA de conformidade");

        var prompt = GetPromptAuditoriaConformidade(transcricao, roteiroConformidade);
        return await _azureOpenAI.GenerateContentAsync(prompt, GetSystemPromptAuditoria());
    }

    /// <summary>
    /// Compara múltiplos relatos entre si para identificar convergências e divergências (inconsistências).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline):
    /// 1. Valida <see cref="IsConfigured"/>; se falso, lança <see cref="InvalidOperationException"/> (fail-fast).
    /// 2. Monta o prompt comparativo via <see cref="GetPromptComparacaoRelatos"/>, que numera os relatos
    ///    (RELATO 1, RELATO 2, ...) e exige tabelas de convergências/divergências com citação dos trechos.
    /// 3. Anexa o <paramref name="contextoUsuario"/> como "CONTEXTO ADICIONAL" e envia ao Azure OpenAI com o
    ///    system prompt de perito forense (<see cref="GetSystemPrompt"/>).
    /// Observação: ao contrário dos demais métodos, este NÃO registra log de início; o restante do fluxo é idêntico.
    /// </remarks>
    /// <param name="relatos">Lista de relatos a comparar; a ordem define a numeração exibida no laudo.</param>
    /// <param name="contextoUsuario">Contexto adicional opcional anexado ao prompt.</param>
    /// <returns>Laudo comparativo em Markdown com convergências, divergências e recomendação de versão mais consistente.</returns>
    /// <exception cref="InvalidOperationException">Lançada quando o Azure OpenAI não está configurado.</exception>
    public async Task<string> CompararRelatos(List<string> relatos, string contextoUsuario = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure OpenAI nao esta configurado.");
        }

        var prompt = GetPromptComparacaoRelatos(relatos);
        return await _azureOpenAI.GenerateContentAsync($"{prompt}\n\nCONTEXTO ADICIONAL: {contextoUsuario}", GetSystemPrompt());
    }

    #region Helpers

    /// <summary>
    /// Normaliza, de forma determinística (sem IA), a nomenclatura dos interlocutores no laudo de oitiva.
    /// </summary>
    /// <remarks>
    /// COMO funciona (passo a passo):
    /// O modelo às vezes rotula os falantes com sinônimos ou com o termo legado "Central". Como o negócio exige
    /// exatamente dois papéis canônicos — **Operador BAS** (quem pergunta) e **Motorista** (quem responde) —
    /// este método aplica uma sequência de <see cref="string.Replace(string,string)"/> sobre o texto retornado:
    ///
    /// Bloco 1 — eliminação do termo legado "Central":
    ///   - "**Central:**" vira o rótulo canônico "**Operador BAS:**";
    ///   - as quatro variações "Operador BAS [/] Central" (com/sem espaços ao redor da barra) colapsam para
    ///     "Operador BAS". As quatro linhas cobrem todas as combinações de espaçamento que o modelo pode emitir,
    ///     já que <c>Replace</c> casa apenas a string exata.
    ///
    /// Bloco 2 — unificação de sinônimos para os dois papéis canônicos:
    ///   - "Atendente", "Operador", "BAS" → "Operador BAS" (quem conduz/pergunta);
    ///   - "Vítima", "Declarante", "Condutor" → "Motorista" (quem relata/responde).
    ///
    /// IMPORTANTE: a busca é sensível à marcação Markdown "**...:**" (negrito + dois-pontos), ou seja, só os
    /// rótulos de fala no início de linha são trocados — menções dessas palavras dentro do corpo do texto
    /// permanecem intactas. A ordem importa: o bloco "Central" roda antes para que o resultado já consolidado
    /// não seja reprocessado pelos sinônimos. Operação puramente textual, idempotente para entradas já normalizadas.
    /// </remarks>
    /// <param name="resultado">Texto do laudo retornado pelo modelo, possivelmente com nomenclatura inconsistente.</param>
    /// <returns>O mesmo laudo com os rótulos de interlocutor padronizados em Operador BAS / Motorista.</returns>
    private string PosProcessarTranscricao(string resultado)
    {
        // Corrige variações comuns de nomenclatura para padronizar
        // (removido termo 'Central' conforme regra de negócio)
        resultado = resultado.Replace("**Central:**", "**Operador BAS:**");
        resultado = resultado.Replace("Operador BAS / Central", "Operador BAS");
        resultado = resultado.Replace("Operador BAS /Central", "Operador BAS");
        resultado = resultado.Replace("Operador BAS/ Central", "Operador BAS");
        resultado = resultado.Replace("Operador BAS/Central", "Operador BAS");

        resultado = resultado.Replace("**Atendente:**", "**Operador BAS:**");
        resultado = resultado.Replace("**Operador:**", "**Operador BAS:**");
        resultado = resultado.Replace("**BAS:**", "**Operador BAS:**");
        resultado = resultado.Replace("**Vítima:**", "**Motorista:**");
        resultado = resultado.Replace("**Declarante:**", "**Motorista:**");
        resultado = resultado.Replace("**Condutor:**", "**Motorista:**");

        return resultado;
    }

    /// <summary>
    /// Prepara o bloco de contexto adicional anexado ao prompt da oitiva, garantindo um valor não-vazio.
    /// </summary>
    /// <remarks>
    /// COMO funciona: se <paramref name="contextoUsuario"/> for nulo/vazio/somente espaços, devolve o marcador
    /// neutro "Nenhum contexto adicional fornecido."; caso contrário, repassa o texto original sem alterações.
    /// PORQUÊ: evita injetar uma seção "CONTEXTO ADICIONAL" vazia no prompt, o que poderia confundir o modelo ou
    /// induzir alucinação por lacuna. O reforço anti-alucinação propriamente dito (exigir citação da fonte) já
    /// vive no system prompt (<see cref="GetSystemPrompt"/>) e no prompt principal; por isso este método é apenas
    /// um saneamento de presença de contexto, e não uma instrução adicional ao modelo.
    /// O nome menciona "anti-alucinação" por razões históricas — hoje sua única responsabilidade é o fallback textual.
    /// </remarks>
    /// <param name="contextoUsuario">Contexto fornecido pelo usuário; pode ser nulo ou vazio.</param>
    /// <returns>O contexto original quando presente, ou um marcador neutro quando ausente.</returns>
    private string GetContextoReforcadoAntiAlucinacao(string contextoUsuario)
    {
        // Contexto simples - a regra anti-alucinação já está no prompt principal
        return string.IsNullOrWhiteSpace(contextoUsuario)
            ? "Nenhum contexto adicional fornecido."
            : contextoUsuario;
    }

    #endregion

    # region Prompts

    /// <summary>
    /// System prompt usado exclusivamente na auditoria de conformidade: define a persona "Auditor Sênior".
    /// </summary>
    /// <remarks>
    /// COMO funciona: retorna um literal fixo que instrui o modelo a adotar postura crítica/detalhista, apontar
    /// falhas de conduta e tom de voz e comparar a fala com o roteiro. É deliberadamente separado de
    /// <see cref="GetSystemPrompt"/> (perito forense) porque auditoria e perícia exigem enquadramentos distintos.
    /// </remarks>
    /// <returns>Texto do system prompt de auditoria.</returns>
    private string GetSystemPromptAuditoria()
    {
        return @"Você é um Auditor Sênior de Qualidade e Conformidade da empresa Opentech.

Sua tarefa é analisar conversas entre Operadores e Motoristas para garantir que os processos internos e normas de segurança foram seguidos.

ESTILO DE ANÁLISE:
- Seja CRÍTICO e DETALHISTA.
- Aponte falhas de conduta, tom de voz (se indicado na transcrição) e omissões.
- Compare a fala com o roteiro de conformidade esperado.";
    }

    /// <summary>
    /// Monta o prompt (mensagem do usuário) da auditoria de conformidade.
    /// </summary>
    /// <remarks>
    /// COMO funciona: interpola o roteiro de referência e a transcrição em seções delimitadas por "---" e define
    /// a TAREFA DO AUDITOR em 4 passos: (1) verificar todos os pontos do roteiro, (2) classificar tom/sentimento,
    /// (3) atribuir SCORE DE CONFIANÇA de 0 a 100% (limiar percentual de aderência) e (4) gerar o laudo de
    /// não-conformidades. Os delimitadores "---" isolam o conteúdo dinâmico das instruções para reduzir ambiguidade.
    /// </remarks>
    /// <param name="transcricao">Transcrição a ser auditada (interpolada na seção 2).</param>
    /// <param name="roteiroConformidade">Roteiro de referência (interpolado na seção 1).</param>
    /// <returns>Prompt de auditoria pronto para envio ao modelo.</returns>
    private string GetPromptAuditoriaConformidade(string transcricao, string roteiroConformidade)
    {
        return $@"AUDITORIA DE CONFORMIDADE E QUALIDADE

# 1. ROTEIRO DE CONFORMIDADE (REFERÊNCIA)
---
{roteiroConformidade}
---

# 2. TRANSCRIÇÃO PARA AUDITORIA
---
{transcricao}
---

# TAREFA DO AUDITOR:
1. Verifique se o Operador seguiu TODOS os pontos do roteiro.
2. Identifique o Tom de Voz e Sentimento (Hesitante, Agressivo, Calmo, Neutro).
3. Atribua um SCORE DE CONFIANÇA (0-100%).
4. Gere um relatório estruturado de não-conformidades.

# RELATÓRIO DE AUDITORIA (LAUDO)
(Gere o laudo técnico aqui...)";
    }

    /// <summary>
    /// System prompt padrão das análises periciais (descrição, oitiva, comparação): define a persona "Perito Forense".
    /// </summary>
    /// <remarks>
    /// COMO funciona: literal fixo que impõe (a) estilo técnico/formal sem coloquialismos, (b) a REGRA
    /// ANTI-ALUCINAÇÃO — para cada informação citar o trecho exato da fonte ou escrever "Não mencionado", nunca
    /// inventar/deduzir — e (c) a definição canônica dos interlocutores: Operador BAS = quem pergunta,
    /// Motorista = quem responde. Essa definição no system prompt é o que orienta o modelo a usar a nomenclatura
    /// que <see cref="PosProcessarTranscricao"/> depois garante de forma determinística.
    /// </remarks>
    /// <returns>Texto do system prompt de perito forense.</returns>
    private string GetSystemPrompt()
    {
        return @"Você é um Perito Forense da empresa Opentech.

ESTILO DE ESCRITA:
- Use linguagem TÉCNICA e FORMAL
- Evite gírias, coloquialismos e expressões informais
- Redação profissional adequada a laudos periciais
- Tom imparcial e objetivo

REGRA ANTI-ALUCINAÇÃO:
- Para CADA informação, CITE o trecho exato da fonte
- Se não há trecho que comprove → escreva ""Não mencionado""
- NUNCA invente, deduza ou assuma dados

INTERLOCUTORES:
- **Operador BAS:** quem FAZ PERGUNTAS
- **Motorista:** quem RESPONDE e RELATA";
    }

    /// <summary>
    /// Monta o prompt de análise de descrição textual (laudo com tabela de dados extraídos).
    /// </summary>
    /// <remarks>
    /// COMO funciona: interpola o tipo e o conteúdo do documento (isolado por "---") e impõe a regra de citar o
    /// trecho original entre aspas para cada dado. Fornece um esqueleto fixo de laudo em 4 seções (Dados Extraídos,
    /// Resumo, Lacunas, Conclusão) com a tabela Veículo/Local/Data-Hora/Envolvidos, instruindo a escrever
    /// "Não mencionado" quando o dado não constar — reforço estrutural à regra anti-alucinação do system prompt.
    /// </remarks>
    /// <param name="descricao">Conteúdo do documento a analisar.</param>
    /// <param name="tipoDocumento">Rótulo do tipo de documento exibido no cabeçalho do prompt.</param>
    /// <returns>Prompt de análise de descrição pronto para envio ao modelo.</returns>
    private string GetPromptAnaliseDescricao(string descricao, string tipoDocumento)
    {
        return $@"Analise o documento abaixo.

REGRA: Para cada dado extraído, CITE o trecho original entre aspas.

DOCUMENTO ({tipoDocumento}):
---
{descricao}
---

# LAUDO DE ANÁLISE

## 1. Dados Extraídos
| Dado | Valor | Citação da Fonte |
|------|-------|------------------|
| Veículo | | ""trecho exato"" |
| Local | | ""trecho exato"" |
| Data/Hora | | ""trecho exato"" |
| Envolvidos | | ""trecho exato"" |

(Se não mencionado, escreva ""Não mencionado"" na coluna Valor)

## 2. Resumo dos Fatos
(Apenas o que está escrito no documento)

## 3. Lacunas e Pontos de Atenção
(O que falta ou precisa verificação)

## 4. Conclusão";
    }

    /// <summary>
    /// Monta o prompt da análise de oitiva, combinando o prompt-base "Detetive" com instruções de formatação.
    /// </summary>
    /// <remarks>
    /// COMO funciona (montagem):
    /// 1. Obtém o prompt-base centralizado em <c>SinistroAPI.Prompts.SinistroPrompts.GetPromptAnaliseOitivaDetetive</c>,
    ///    passando a <paramref name="duracao"/> (o conteúdo investigativo do laudo vive nessa classe de prompts,
    ///    não aqui).
    /// 2. Acrescenta uma INSTRUÇÃO ADICIONAL exigindo, no início do laudo, uma tabela "DADOS IDENTIFICADOS"
    ///    (Placa, Transportadora, Motorista, CPF, Data/Hora, Telefone, Ramal, Operador) com "__________" para
    ///    campos não encontrados, seguida do marcador literal "---SEPARADOR_DADOS---".
    /// 3. Anexa a <paramref name="transcricao"/> ao final.
    /// PORQUÊ do separador: é um token sentinela que a camada consumidora usa para fatiar a resposta em
    /// "cabeçalho de dados" e "corpo do laudo"; precisa ser estável e improvável de aparecer no texto natural.
    /// Observação: <paramref name="tipoOperacao"/> é recebido para manter a assinatura/contrato, mas o
    /// enquadramento principal vem do prompt-base "Detetive".
    /// </remarks>
    /// <param name="transcricao">Transcrição a analisar, anexada ao fim do prompt.</param>
    /// <param name="duracao">Duração da ligação repassada ao prompt-base "Detetive".</param>
    /// <param name="tipoOperacao">Tipo de operação (ex.: "Viagem"); contextualiza a análise.</param>
    /// <returns>Prompt completo de oitiva pronto para envio ao modelo.</returns>
    private string GetPromptAnaliseOitiva(string transcricao, string duracao, string tipoOperacao)
    {
        // Usa o novo prompt "Detetive" definido em SinistroPrompts
        var promptBase = SinistroAPI.Prompts.SinistroPrompts.GetPromptAnaliseOitivaDetetive(duracao);

        return $@"{promptBase}

INSTRUÇÃO ADICIONAL:
Gere uma tabela de DADOS IDENTIFICADOS no início do laudo.
Use o separador ---SEPARADOR_DADOS--- logo após a tabela para separar do resto do laudo.

FORMATO ESPERADO:
# DADOS IDENTIFICADOS
| Campo | Valor Identificado |
|-------|-------------------|
| Placa do Veículo | [Valor ou __________ se não encontrado] |
| Transportadora | [Valor ou __________ se não encontrado] |
| Motorista | [Valor ou __________ se não encontrado] |
| CPF | [Valor ou __________ se não encontrado] |
| Data/Hora da Ligação | [Valor ou __________ se não encontrado] |
| Telefone | [Valor ou __________ se não encontrado] |
| Ramal | [Valor ou __________ se não encontrado] |
| Operador de Registro | [Valor ou __________ se não encontrado] |

---SEPARADOR_DADOS---

# LAUDO PERICIAL
(O restante do laudo segue aqui...)

TRANSCRIÇÃO PARA ANÁLISE:
{transcricao}";
    }

    /// <summary>
    /// Monta o prompt de comparação de relatos, numerando cada relato e definindo o esqueleto do laudo.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Percorre <paramref name="relatos"/> com um <see cref="StringBuilder"/>, emitindo para cada item um
    ///    cabeçalho "### RELATO {i+1}:" (numeração 1-based, derivada do índice 0-based) seguido do texto e de um
    ///    delimitador "---". Essa rotulagem dá ao modelo âncoras estáveis para referenciar nas tabelas.
    /// 2. Interpola o bloco formatado e instrui o modelo a citar trechos ao apontar convergências/divergências,
    ///    com seções fixas (Convergências, Divergências, Conclusão) — as colunas de exemplo citam "Relato 1/Relato 2",
    ///    mas a numeração real acompanha a quantidade de itens recebidos.
    /// </remarks>
    /// <param name="relatos">Lista de relatos a comparar; cada um vira um bloco numerado no prompt.</param>
    /// <returns>Prompt de comparação pronto para envio ao modelo.</returns>
    private string GetPromptComparacaoRelatos(List<string> relatos)
    {
        var relatosFormatados = new StringBuilder();
        for (int i = 0; i < relatos.Count; i++)
        {
            relatosFormatados.AppendLine($"### RELATO {i + 1}:");
            relatosFormatados.AppendLine(relatos[i]);
            relatosFormatados.AppendLine("---");
        }

        return $@"Compare os relatos abaixo. CITE trechos ao apontar convergências/divergências.

{relatosFormatados}

# 1. CONVERGÊNCIAS
| Aspecto | Relato 1 diz | Relato 2 diz |
|---------|--------------|--------------|

# 2. DIVERGÊNCIAS
| Aspecto | Relato 1 diz | Relato 2 diz |
|---------|--------------|--------------|

# 3. CONCLUSÃO
* **Versão mais consistente:**
* **Recomendação:**";
    }

    #endregion
}
