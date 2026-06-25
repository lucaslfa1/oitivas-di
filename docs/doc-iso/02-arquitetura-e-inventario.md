# 02 - Arquitetura e Inventario Tecnico

## Visao geral

O Sentinel e composto por quatro blocos principais:

```text
Usuario
  -> Frontend estatico em Backend/wwwroot
  -> Backend ASP.NET Core 8
  -> Servicos de IA e media processor
  -> SQLite / arquivos / dashboard
```

## Componentes

| Componente | Caminho | Tecnologia | Responsabilidade |
|---|---|---|---|
| Backend API | `Backend/` | ASP.NET Core 8 | API REST, CORS, DI, controllers, banco, SignalR, orquestracao. |
| Frontend | `Backend/wwwroot/` | HTML/CSS/JS modular | Upload, navegacao, chamada de API, exibicao, edicao, exportacao. |
| Media Processor | `sentinel-cortex/` | Python FastAPI | Normalizacao/conversao de audio, merge, keyframes, sentimento acustico. |
| Dashboard KPI | `dashboard-kpi/` | Dash/Flask/Pandas | Indicadores de envio, ranking de operadores e filtros. |
| Persistencia | `Backend/Data/AppDbContext.cs` | EF Core + SQLite | Analises salvas e usuarios. |
| Deploy | `Dockerfile`, `Backend/Dockerfile`, `sentinel-cortex/Dockerfile`, `docker-compose.yml` | Docker/Cloud Run | Execucao e publicacao dos servicos. |

## Backend ASP.NET Core

### Inicializacao

Arquivo: `Backend/Program.cs`

Responsabilidades:

- Carrega `appsettings.Local.json` opcional.
- Carrega `Configuration/prompts.json` e `Configuration/text_corrections.json`.
- Configura limite de request e multipart.
- Registra services de IA, media processor, banco, SignalR, controllers e Swagger.
- Inicializa SQLite por `EnsureCreated`.
- Cria usuarios seed se necessario.
- Mapeia `/health`, controllers e hub `/hubs/analysis`.

### Controllers

| Controller | Rotas principais | Processo |
|---|---|---|
| `TranscricaoController` | `POST /api/transcrever`, `POST /api/analisar/laudo`, `POST /api/analisar/oitiva`, `POST /api/auditar`, `POST /api/extrair-dados` | Transcricao, laudo, auditoria de conformidade e extracao. |
| `AnaliseController` | `POST /api/analisar/imagem`, `POST /api/analisar/video` | Analise de foto e video. |
| `AnalisesController` | `POST /api/salvar`, `GET /api/analises`, `GET/DELETE /api/analises/{id}` | Persistencia e consulta de analises. |
| `AuthController` | `POST /api/auth/login`, `POST /api/auth/register` | Login e registro simples. |
| `HealthController` | `GET /api/health`, `/api/health/live`, `/api/health/ready` | Health, liveness, readiness. |
| `ToolsController` | `POST /api/tools/merge-audio` | Merge de multiplos audios. |
| `FileStorageController` | `POST /api/storage/upload` | Upload local para `wwwroot/uploads/audio`. |

## Servicos de IA e midia

| Servico | Caminho | Papel |
|---|---|---|
| `TranscricaoOrquestradorService` | `Backend/Services/TranscricaoOrquestradorService.cs` | Orquestra Azure Speech-to-Text e fallback Whisper; envia progresso por SignalR. |
| `AzureFastTranscricaoService` | `Backend/Services/AzureFastTranscricaoService.cs` | Fast Transcription API com diarizacao e phrase list. |
| `AzureWhisperService` | `Backend/Services/AzureWhisperService.cs` | Fallback Whisper via Azure OpenAI, com timestamps e filtros anti-ruido. |
| `SpeakerDetectionService` | `Backend/Services/SpeakerDetectionService.cs` | Heuristicas para Operador BAS vs Motorista e limpeza de segmentos. |
| `DescricaoAnaliseService` | `Backend/Services/DescricaoAnaliseService.cs` | Gera laudos, auditorias e comparacoes usando Azure OpenAI. |
| `AzureOpenAIService` | `Backend/Services/AzureOpenAIService.cs` | Wrapper de chat completions e visao no Azure OpenAI. |
| `ImagemAnaliseService` | `Backend/Services/ImagemAnaliseService.cs` | Analise de vistoria por imagem. |
| `VideoAnaliseService` | `Backend/Services/VideoAnaliseService.cs` | Upload e analise de video via Gemini File API ou Vertex AI. |
| `MediaProcessorService` | `Backend/Services/MediaProcessorService.cs` | Cliente HTTP para `sentinel-cortex`. |
| `AzureTextAnalyticsService` | `Backend/Services/AzureTextAnalyticsService.cs` | Analise textual de sentimento como complemento. |

## Frontend

Entrada principal: `Backend/wwwroot/index.html` carrega `js/app.js`.

Arquitetura JS:

- `js/api/sinistroApi.js`: chamadas HTTP.
- `js/core`: estado, drafts, renderizacao, tema, storage, utilitarios.
- `js/ui`: upload, modal, navegacao, toast, player, waveform.
- `js/services/analise`: transcricao, laudo, foto/video e edicao inline.
- `js/features`: salvos e merge de audio.

Fluxos principais:

- Audio: arquivo -> `/api/transcrever` -> transcricao -> `/api/analisar/laudo` -> laudo.
- Foto: arquivo -> `/api/analisar/imagem` -> laudo de vistoria.
- Video: arquivo -> `/api/analisar/video` -> laudo de video.
- Merge: multiplos arquivos -> `/api/tools/merge-audio` -> arquivo mergeado -> transcricao.
- Salvos: CRUD via `/api/salvar` e `/api/analises`.

## Banco de dados

Arquivo: `Backend/Data/AppDbContext.cs`

Tabelas:

| Tabela | Campos principais | Observacao |
|---|---|---|
| `Analises` | `Id`, `Tipo`, `Conteudo`, `Arquivo`, `Data` | Persistencia de laudos/transcricoes. |
| `Users` | `Id`, `Username`, `Password`, `Role` | Implementacao atual usa senha em texto claro. |

Configuracao:

- `DB_PATH` por variavel de ambiente.
- Fallback: `sinistros.db`.
- `EnsureCreated()` no startup.

## Sentinel Cortex

Arquivo de entrada: `sentinel-cortex/main.py`.

Rotas principais:

| Rota | Finalidade |
|---|---|
| `/health` e `/api/v1/health` | Health check. |
| `/process/audio` | Normalizacao/conversao de audio. |
| `/process/merge-audio` | Junta multiplos audios e retorna MP3. |
| `/process/video` | Extrai keyframes. |
| `/extract/audio` | Extrai audio de video. |
| `/tools/convert-to-wav` | Conversao manual para WAV. |
| `/analyze/sentiment` | Analise acustica de sentimento. |
| `/cache/stats`, `/cache/clear`, `/cache/cleanup` | Cache em memoria. |

## Dashboard KPI

Arquivo: `dashboard-kpi/app_dashboard.py`.

Responsabilidades:

- Carregar dados Excel/CSV de BI.
- Validar login consultando `BACKEND_AUTH_URL`.
- Restringir acesso por roles: `Admin`, `Coordenador`, `Supervisor`, `Analista`.
- Exibir KPIs, graficos por escala, motivos, timeline e ranking de operadores.

## Infraestrutura e deploy

| Artefato | Finalidade |
|---|---|
| `docker-compose.yml` | Sobe backend, sentinel-cortex e dashboard. |
| `Dockerfile` | Build multi-stage do backend a partir da raiz. |
| `Backend/Dockerfile` | Dockerfile especifico do backend. |
| `sentinel-cortex/Dockerfile` | Python 3.11 slim + ffmpeg + requirements. |
| `deploy.ps1` | Bump de versao do frontend, commit/push e deploy Cloud Run. |
| `setup_gcp.ps1` | Script de infraestrutura GCP planejada: Run, Storage, Firestore, Tasks, Eventarc. |

## Divergencias documentais identificadas

As referencias existentes citam combinacoes diferentes de infraestrutura e provider:

- `README.md`: Cloud Run, Gemini, SQLite.
- `ARQUITETURA.md`: Azure Web App, Azure OpenAI GPT-4, Gemini.
- Codigo atual: Azure Speech, Azure OpenAI, Azure Text Analytics, Gemini/Vertex opcionais, Cloud Run em scripts.

Para auditoria, considerar o codigo como fonte primaria e registrar decisao formal de arquitetura alvo.

