# 08 - Snippets de Codigo Auditaveis

Os snippets abaixo sao curtos e mascarados quando envolvem credenciais. Eles servem como evidencia de controle, nao como substituto de revisao completa do codigo.

## Inicializacao e limites de upload

Arquivo: `Backend/Program.cs`

```csharp
var uploadLimits = builder.Configuration
    .GetSection("UploadLimits")
    .Get<UploadLimitsOptions>() ?? new UploadLimitsOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = uploadLimits.MaxRequestBodyBytes;
});

builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = uploadLimits.MaxRequestBodyBytes;
});
```

Controle associado:

- ISO 8.1 e 8.5: controle operacional de entradas grandes.
- Evidencia de limite centralizado para upload.

## Registro de banco SQLite

Arquivo: `Backend/Program.cs`

```csharp
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "sinistros.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
```

Controle associado:

- ISO 7.1: recurso de persistencia identificado.
- Risco: em container, requer volume persistente ou banco gerenciado.

## Health check completo

Arquivo: `Backend/Controllers/HealthController.cs`

```csharp
var dbHealthy = await _db.Database.CanConnectAsync();

if (_mediaProcessor.IsEnabled)
{
    pythonAvailable = await _mediaProcessor.IsAvailableAsync();
}

var overallStatus = dbHealthy ? "healthy" : "unhealthy";
if (dbHealthy && _mediaProcessor.IsEnabled && !pythonAvailable)
{
    overallStatus = "degraded";
}
```

Controle associado:

- ISO 9.1: monitoramento e medicao.
- Permite diferenciar falha critica de banco e degradacao do media processor.

## Validacao de audio

Arquivo: `Backend/Controllers/TranscricaoController.cs`

```csharp
if (dados.Arquivo == null || dados.Arquivo.Length == 0)
    return BadRequest(new ErrorResponse("Nenhum arquivo."));

if (!dados.Arquivo.ContentType.StartsWith("audio/") &&
    !dados.Arquivo.ContentType.StartsWith("video/webm") &&
    !dados.Arquivo.ContentType.StartsWith("video/mp4"))
    return BadRequest(new ErrorResponse("Formato invalido."));

if (dados.Arquivo.Length > _uploadLimits.MaxAudioUploadBytes)
    return StatusCode(413, new ErrorResponse("Arquivo de audio excede o limite configurado."));
```

Controle associado:

- ISO 8.2: requisitos de entrada.
- ISO 8.7: controle de saidas nao conformes por erro padronizado.

## Orquestracao de transcricao

Arquivo: `Backend/Services/TranscricaoOrquestradorService.cs`

```csharp
if (_azureSpeechToText.IsConfigured)
{
    var resultadoStt = (await _azureSpeechToText
        .TranscreverAsync(audioBytes, mimeType) ?? string.Empty).Trim();

    if (TranscricaoPareceValida(resultadoStt))
    {
        return resultadoStt;
    }
}

if (_azureWhisper.IsConfigured)
{
    return await _azureWhisper.TranscreverAsync(audioBytes, mimeType);
}
```

Controle associado:

- ISO 8.5: provisao controlada do servico.
- Reduz indisponibilidade por fallback.

## Heuristica anti-transcricao fraca

Arquivo: `Backend/Services/TranscricaoOrquestradorService.cs`

```csharp
if (linhas.Count <= 2 && transcricao.Length < 180)
{
    return false;
}

if (frases.Count >= 8 && repeticaoDominante >= 0.70)
{
    return false;
}
```

Controle associado:

- ISO 8.6: criterio tecnico para aceitar ou rejeitar resultado intermediario.
- Mitiga transcricoes vazias ou repetitivas.

## Temperatura zero no laudo

Arquivo: `Backend/Services/AzureOpenAIService.cs`

```csharp
var payload = new
{
    messages = messages,
    max_tokens = 4096,
    temperature = 0.0,
    top_p = 1
};
```

Controle associado:

- ISO 8.5 e 8.6: consistencia e repetibilidade.
- Mitiga variacao indevida em laudos periciais.

## Prompt anti-alucinacao

Arquivo: `Backend/Services/DescricaoAnaliseService.cs`

```csharp
return @"Voce e um Perito Forense da empresa Opentech.

REGRA ANTI-ALUCINACAO:
- Para CADA informacao, CITE o trecho exato da fonte
- Se nao ha trecho que comprove -> escreva ""Nao mencionado""
- NUNCA invente, deduza ou assuma dados";
```

Controle associado:

- ISO 8.2 e 8.5: requisitos do produto e controle de saida.
- Define criterio textual obrigatorio para laudos.

## Persistencia de analise

Arquivo: `Backend/Controllers/AnalisesController.cs`

```csharp
[HttpPost("salvar")]
public async Task<IActionResult> Salvar([FromBody] AnaliseModel model)
{
    model.Data = DateTime.Now;
    _db.Analises.Add(model);
    await _db.SaveChangesAsync();

    return Ok(new OperacaoResponse(model.Id, "Analise salva com sucesso!"));
}
```

Controle associado:

- ISO 7.5: informacao documentada.
- Lacuna: nao registra usuario, aprovador, versao ou historico de edicao.

## Login atual

Arquivo: `Backend/Controllers/AuthController.cs`

```csharp
var user = await _context.Users
    .FirstOrDefaultAsync(u =>
        u.Username == request.Username &&
        u.Password == request.Password);
```

Controle associado:

- Evidencia de autenticacao existente.
- Nao conformidade: senha em texto claro e ausencia de hash/token.

## Frontend chamando laudo

Arquivo: `Backend/wwwroot/js/api/sinistroApi.js`

```javascript
const response = await fetch(`${API_BASE}/analisar/laudo`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
        Transcricao: transcricao,
        Duracao: duracao,
        Contexto: contexto
    })
});
```

Controle associado:

- Evidencia do contrato frontend-backend.
- Facilita teste de integracao ponta a ponta.

## Media processor client

Arquivo: `Backend/Services/MediaProcessorService.cs`

```csharp
var response = await _httpClient.PostAsync("/process/audio", content);

if (!response.IsSuccessStatusCode)
{
    _logger.LogWarning("Falha no pre-processamento de audio");
    return null;
}
```

Controle associado:

- Fallback operacional: falha do Python nao quebra transcricao.

## Sentinel Cortex - merge de audios

Arquivo: `sentinel-cortex/routers/v1/media.py`

```python
@router.post("/process/merge-audio")
async def merge_audio(files: list[UploadFile] = File(...)):
    audio_files_data = []
    for file in files:
        content = await file.read()
        audio_files_data.append((content, file.content_type or ""))

    merged, metadata = await audio_proc.merge_audios(audio_files_data)
    return Response(content=merged, media_type="audio/mpeg")
```

Controle associado:

- ISO 8.5: processo de unificacao de evidencias de audio.
- Risco: requer teste com arquivos corrompidos e limite de tamanho.

## Dashboard - controle por role

Arquivo: `dashboard-kpi/app_dashboard.py`

```python
ALLOWED_DASHBOARD_ROLES = [
    'Admin',
    'Coordenador',
    'Supervisor',
    'Analista'
]

if user_role in ALLOWED_DASHBOARD_ROLES:
    session['user'] = result.get('username')
    session['role'] = user_role
    return redirect('/')
```

Controle associado:

- ISO 7.1 e 9.1: acesso a indicadores por perfil autorizado.
- Lacuna: depende de autenticacao backend ainda simples.

## Configuracao segura recomendada

Snippet recomendado, nao copiar segredos reais:

```json
{
  "AzureOpenAI": {
    "Enabled": true,
    "ApiKey": "<AZURE_OPENAI_API_KEY>",
    "Endpoint": "https://<resource>.openai.azure.com/",
    "DeploymentName": "gpt-4o"
  }
}
```

Controle associado:

- Arquivos versionados devem conter placeholders.
- Valores reais devem vir de variaveis de ambiente ou gerenciador de segredos.

