using Google.Cloud.AIPlatform.V1;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Interfaces;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço especializado em análise de DESCRIÇÕES textuais de sinistros
/// Útil para analisar boletins de ocorrência, relatos escritos, etc.
/// </summary>
public class DescricaoAnaliseService : IDescricaoAnaliseService
{
    private readonly HttpClient _httpClient;
    private readonly string _geminiApiKey;
    private readonly ILogger<DescricaoAnaliseService> _logger;
    private readonly VertexAIService _vertexAI;
    private readonly AzureOpenAIService _azureOpenAI;
    private readonly VertexAISettings _vertexSettings;
    private readonly string _reportModel;

    public DescricaoAnaliseService(HttpClient httpClient, IConfiguration configuration, ILogger<DescricaoAnaliseService> logger, VertexAIService vertexAI, AzureOpenAIService azureOpenAI, IOptions<VertexAISettings> vertexSettings)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _geminiApiKey = configuration["GEMINI_API_KEY"] ?? "";
        _logger = logger;
        _vertexAI = vertexAI;
        _azureOpenAI = azureOpenAI;
        _vertexSettings = vertexSettings.Value;
        _reportModel = _vertexSettings.Models.ReportGeneration ?? "gemini-1.5-pro";
    }

    /// <summary>
    /// Verifica se o serviço está configurado (Azure, Gemini ou Vertex AI)
    /// </summary>
    public bool IsConfigured => _azureOpenAI.IsConfigured;

    /// <summary>
    /// Analisa descrição textual de sinistro
    /// </summary>
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
    /// Analisa transcrição de oitiva
    /// </summary>
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
    /// Audita a conformidade de uma conversa com base em um roteiro
    /// </summary>
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
    /// Compara múltiplos relatos para identificar inconsistências
    /// </summary>
    public async Task<string> CompararRelatos(List<string> relatos, string contextoUsuario = "")
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure OpenAI nao esta configurado.");
        }

        var prompt = GetPromptComparacaoRelatos(relatos);
        return await _azureOpenAI.GenerateContentAsync($"{prompt}\n\nCONTEXTO ADICIONAL: {contextoUsuario}", GetSystemPrompt());
    }

    #region Gemini (Fallback)

    private async Task<string> AnalisarComGemini(string descricao, string tipoDocumento, string contextoUsuario)
    {
        var prompt = GetPromptAnaliseDescricao(descricao, tipoDocumento);
        return await EnviarParaGemini(prompt, contextoUsuario);
    }    

    private async Task<string> AnalisarOitivaComGemini(string transcricao, string duracao, string contextoUsuario, string tipoOperacao)
    {
        var prompt = GetPromptAnaliseOitiva(transcricao, duracao, tipoOperacao);
        var contextoReforçado = GetContextoReforcadoAntiAlucinacao(contextoUsuario);
        
        var resultado = await EnviarParaGemini(prompt, contextoReforçado);
        
        // Pós-processar para corrigir possíveis inversões de interlocutores
        return PosProcessarTranscricao(resultado);
    }

    /// <summary>
    /// Corrige possíveis erros na transcrição formatada
    /// </summary>
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
    /// Gera contexto adicional para a análise
    /// </summary>
    private string GetContextoReforcadoAntiAlucinacao(string contextoUsuario)
    {
        // Contexto simples - a regra anti-alucinação já está no prompt principal
        return string.IsNullOrWhiteSpace(contextoUsuario) 
            ? "Nenhum contexto adicional fornecido." 
            : contextoUsuario;
    }

    private async Task<string> EnviarParaGemini(string prompt, string contextoUsuario)
    {
        var payload = new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"{GetSystemPrompt()}\n\n{prompt}\n\nCONTEXTO ADICIONAL: {contextoUsuario}" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.0,  // ZERO para máxima precisão e mínima alucinação
                maxOutputTokens = 8192
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        // Usa modelo configurado com temperatura 0.0 para máxima precisão
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_reportModel}:generateContent?key={_geminiApiKey}";
        
        var resposta = await _httpClient.PostAsync(url, jsonContent);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync();
            throw new Exception($"Erro Gemini ({resposta.StatusCode}): {erro}");
        }

        var respostaString = await resposta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respostaString);
        
        return doc.RootElement.GetProperty("candidates")[0]
                              .GetProperty("content")
                              .GetProperty("parts")[0]
                              .GetProperty("text")
                              .GetString() ?? "Sem resposta.";
    }

    #endregion

    # region Prompts

    private string GetSystemPromptAuditoria()
    {
        return @"Você é um Auditor Sênior de Qualidade e Conformidade da empresa Opentech.

Sua tarefa é analisar conversas entre Operadores e Motoristas para garantir que os processos internos e normas de segurança foram seguidos.

ESTILO DE ANÁLISE:
- Seja CRÍTICO e DETALHISTA.
- Aponte falhas de conduta, tom de voz (se indicado na transcrição) e omissões.
- Compare a fala com o roteiro de conformidade esperado.";
    }

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
