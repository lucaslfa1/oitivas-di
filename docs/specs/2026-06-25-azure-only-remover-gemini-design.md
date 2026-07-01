# Azure-only: remover Gemini/Vertex e reativar análise de mídia

**Data:** 2026-06-25
**Status:** Aprovado para implementação

## Problema

A análise de mídia quebrou após a remoção da chave Gemini (commit `71d341e`):

- **Vídeo** (`/analisar/video`) retorna `Serviço de vídeo não configurado. Verifique GEMINI_API_KEY` — o
  `VideoAnaliseService` só tem caminho Gemini (chave vazia) e Vertex (desabilitado).
- **Imagem** (`/analisar/imagem`) já está cabeada para o Azure OpenAI (GPT-4o Vision), que está
  `Enabled: true` no `appsettings.json`.

## Objetivo

Tornar o backend **100% Azure** para análise de mídia, **removendo todo o código Gemini e Vertex AI**.
Imagem e vídeo passam a usar exclusivamente o Azure OpenAI (GPT-4o).

## Restrição técnica

O GPT-4o **não ingere arquivo de vídeo** — só imagens. O vídeo é analisado via **keyframes**: o
`MediaProcessorService` (serviço Python/ffmpeg em `localhost:8000`, já existente e usado pela
transcrição) extrai N quadros, que são enviados como imagens ao GPT-4o.

> Risco conhecido: a extração de keyframes ainda não foi testada pelo usuário. Se o serviço Python
> estiver indisponível, o endpoint de vídeo deve falhar com **mensagem clara**, não com erro genérico.

## Mudanças

### Remover (Gemini)
- `Services/AI/GeminiProvider.cs`, `Services/AI/IAIProvider.cs` — registrados mas sem consumidores.
- `Services/GeminiFileApiService.cs` — usado só pelo GeminiProvider.
- `#region Gemini (Fallback)` em `DescricaoAnaliseService` — código morto.
- `GEMINI_API_KEY` do `appsettings.json` e dos serviços.

### Remover (Vertex AI — Google Cloud)
- `Services/VertexAIService.cs`.
- Classes `VertexAISettings` e `ModelSettings` em `Configuration/AppSettings.cs`.
- Seção `VertexAI` do `appsettings.json` e `Configure<VertexAISettings>` no `Program.cs`.
- `MaxVertexInlineMB`/`MaxVertexInlineBytes` em `UploadLimitsOptions` + `appsettings.json`.
- Pacote NuGet `Google.Cloud.AIPlatform.V1` do `.csproj`.

### Alterar (Azure)
- **`AzureOpenAIService`**: hoje aceita só 1 imagem e chumba `image/jpeg`. Adicionar:
  - respeito ao mime real na imagem única;
  - método `GenerateVisionAsync(prompt, systemPrompt, List<(base64, mime)>, maxTokens)` para
    **múltiplos quadros** (keyframes), com `max_tokens` maior (8192) para laudos.
- **`ImagemAnaliseService`**: Azure-only. `IsConfigured => _azureOpenAI.IsConfigured`. Remove
  HttpClient/Gemini/Vertex. `AnalisarMultiplasImagens` passa a usar `GenerateVisionAsync`.
- **`VideoAnaliseService`**: Azure-only. `IsConfigured => _azureOpenAI.IsConfigured`.
  `AnalisarVideoStream`: lê bytes → `ExtractKeyframesAsync` → se vazio, lança erro claro; senão envia
  os frames + prompt de vídeo ao GPT-4o. Remove todo o upload Gemini File API e o método legado.
- **`AnaliseController`**: mensagem de erro do vídeo refletindo Azure (não "GEMINI_API_KEY").
- **`Program.cs`**: remover registros Gemini/Vertex; trocar typed-HttpClients de Imagem/Vídeo/Descrição
  por `AddScoped` (não usam mais HttpClient próprio); ajustar `using`/comentário de cabeçalho.

### Sem mudança
- Frontend (`sinistroApi.js`, `processarMidia.js`) — já chama os endpoints corretos.
- Pipeline de transcrição (Azure Whisper/Speech) — não tocado.

## Verificação
1. `dotnet build` sem erros (sem referências pendentes a Gemini/Vertex).
2. `dotnet test` (testes de DB seguem passando — não tocam IA).
3. App sobe; `/analisar/imagem` gera laudo via Azure.
4. `/analisar/video` com o Python ativo gera laudo via keyframes; com Python fora do ar, erro claro.
