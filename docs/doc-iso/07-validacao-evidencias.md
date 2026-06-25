# 07 - Validacao, Testes e Evidencias

## Ambiente usado para verificacao

| Item | Valor |
|---|---|
| Data | 2026-05-18 |
| Workspace | `D:\sentinel-open` |
| Branch | `main` |
| Commit | `cd4e0e5` |
| Sistema operacional | Windows |
| Shell usado | `cmd.exe` |

## Verificacao Git

Estado antes da criacao desta documentacao:

```text
## main...origin/main
 D fresh_cortex.txt
 D fresh_errors.txt
 D fresh_logs.txt
```

Observacao: as tres delecoes ja existiam antes desta documentacao e nao foram revertidas.

## Teste C# executado

Comando:

```powershell
dotnet test Backend\Backend.Tests\Backend.Tests.csproj
```

Resultado:

```text
Total: 6
Aprovado: 4
Falha: 2
Ignorado: 0
```

Falhas:

- `AnalisesControllerTests.Salvar_SetsDateAndReturnsId`
- `AnalisesControllerTests.Listar_LimitsTo50`

Causa tecnica observada:

```text
System.MissingMethodException:
Method not found:
Microsoft.EntityFrameworkCore.Diagnostics.AbstractionsStrings.ArgumentIsEmpty(System.Object)
```

Interpretacao:

O projeto principal usa EF Core 8 no `Backend/SinistroAPI.csproj`, enquanto `Backend.Tests` usa `TargetFramework net10.0` e `Microsoft.EntityFrameworkCore.InMemory 10.0.1`. A falha e consistente com desalinhamento de versoes entre EF Core relacional e InMemory usado nos testes.

Avisos de build observados:

- Campo `_corrections` nao anulavel em `AzureFastTranscricaoService`.
- Possivel referencia nula em `AzureTextAnalyticsService`.
- Campo `_predictionClient` nao anulavel em `VertexAIService`.

## Checagem Python executada

Primeira tentativa:

```powershell
python -m compileall sentinel-cortex -q
```

Resultado: encerrada manualmente porque varreu tambem `.venv`, escopo grande e nao necessario para evidencia do codigo versionado.

Segunda tentativa, escopo fonte:

```powershell
python -m py_compile sentinel-cortex\main.py sentinel-cortex\core\config.py sentinel-cortex\core\security.py sentinel-cortex\models\responses.py sentinel-cortex\routers\api.py sentinel-cortex\routers\v1\auth.py sentinel-cortex\routers\v1\intelligence.py sentinel-cortex\routers\v1\media.py sentinel-cortex\routers\v1\system.py sentinel-cortex\services\audio_processor.py sentinel-cortex\services\cache_service.py sentinel-cortex\services\quality_analyzer.py sentinel-cortex\services\sentiment_analyzer.py sentinel-cortex\services\video_processor.py
```

Resultado:

```text
Exit code 0
```

Interpretacao:

Os arquivos Python versionados compilam sintaticamente. Essa checagem nao cobre erros runtime, como a referencia a `start_trim` em `audio_processor.py`.

## Evidencias de validacao existentes no repositorio

| Evidencia | Caminho | Descricao |
|---|---|---|
| Testes de controller de analises | `Backend/Backend.Tests/AnalisesControllerTests.cs` | Valida salvamento e limite de 50 registros. |
| Testes de transcricao | `Backend/Backend.Tests/TranscricaoControllerTests.cs` | Valida ausencia de arquivo, MIME invalido, servico nao configurado e limite de tamanho. |
| Teste de integracao Python | `test_integration.py` | Testa transcricao e auditoria contra backend local. |
| Scripts PowerShell de teste | `test_*.ps1`, `run_audit_test.ps1` | Chamadas locais para endpoints e validacao manual. |
| Verificacao de ambiente | `verify_install.ps1` | Checa Git, Node, Python, .NET, Docker, WSL, gcloud e Azure CLI. |
| Health backend | `Backend/Controllers/HealthController.cs` | DB e media processor. |
| Health Cortex | `sentinel-cortex/routers/v1/system.py` | Status, versao e cache. |

## Criterios de aceite recomendados antes de auditoria externa

| Area | Criterio |
|---|---|
| Build | `dotnet build Backend\SinistroAPI.csproj` sem erro. |
| Testes C# | `dotnet test` 100% aprovado. |
| Python | `py_compile` e smoke test dos endpoints `/health`, `/process/audio`, `/process/merge-audio`. |
| Frontend | Smoke test de audio, foto, video, salvos e merge em navegador. |
| Seguranca | Nenhum segredo real em arquivos versionados. |
| Autenticacao | Hash de senha, RBAC e endpoints protegidos. |
| Evidencia de laudo | Laudo com transcricao, fonte, responsavel, data, revisao e status. |
| Auditoria | Audit log para acoes criticas. |
| Deploy | Release aprovado, versionado e com rollback documentado. |

## Plano de testes recomendado

### Testes unitarios

- Validacao de DTOs e limites de upload.
- Validacao de heuristicas de speaker detection.
- Validacao de parsing de Azure Speech-to-Text e Whisper.
- Validacao de prompts e separador de dados.
- Validacao de persistencia SQLite com EF Core alinhado.

### Testes de integracao

- `POST /api/transcrever` com audio curto valido.
- `POST /api/transcrever` com MIME invalido.
- `POST /api/analisar/laudo` com transcricao vazia.
- `POST /api/analisar/imagem` com arquivo nao imagem.
- `POST /api/tools/merge-audio` com um arquivo e com dois arquivos.
- `GET /api/health` com media processor habilitado/desabilitado.

### Testes de seguranca

- Autenticacao obrigatoria por endpoint.
- Tentativa de role indevida.
- Upload de arquivo grande.
- Upload com content type manipulado.
- CORS por origem nao autorizada.
- Secret scan no repositorio.

### Testes de operacao

- Deploy em ambiente homologacao.
- Health check pos-deploy.
- Geracao de laudo completo.
- Salvamento e recuperacao.
- Exportacao PDF/Word no frontend.
- Validacao do dashboard KPI.

