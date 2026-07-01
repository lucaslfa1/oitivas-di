# Sentinel — Sistema de Análise Forense de Sinistros

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Python 3.11](https://img.shields.io/badge/Python-3.11-3776AB.svg)](https://www.python.org/)
[![Azure](https://img.shields.io/badge/Azure-OpenAI%20%7C%20Speech-0078D4.svg)](https://azure.microsoft.com/)

Sistema de análise forense de sinistros veiculares com IA (Azure):

- **Transcrição de Oitivas** — áudios de ligações (Operador BAS ↔ Motorista) em texto estruturado, com diarização.
- **Análise de Imagens e Vídeos** — danos em veículos, dashcams e depoimentos.
- **Geração de Laudos** — relatórios técnicos periciais automatizados.
- **Detecção de Inconsistências** — apoio à identificação de fraudes.

> **IA = Azure-only:** Azure Speech-to-Text + Azure OpenAI (GPT-4o / Whisper) + Azure Text Analytics. Versões antigas usavam Google Gemini/Vertex — removidas.

## Arquitetura

Monorepo com 3 aplicações deployáveis:

```
oitiva-di-refatorada/
├─ services/
│  ├─ api/         # API principal — ASP.NET Core 8 (SinistroAPI) + SPA (wwwroot) + EF Core/SQLite + SignalR
│  ├─ cortex/      # Microsserviço Python/FastAPI (sentinel-cortex): pré-processamento de mídia (ffmpeg, keyframes)
│  └─ dashboard/   # Dashboard de KPIs — Python/Dash (Flask + gunicorn)
├─ deploy/         # Scripts de deploy (Cloud Run) e provisionamento GCP
├─ scripts/tools/  # Utilitários
└─ docs/           # Arquitetura, documentação ISO e plano de melhoria
```

Fluxo: a **api** recebe a mídia, delega o pré-processamento ao **cortex**, transcreve via Azure Speech/Whisper, gera laudos via Azure OpenAI (GPT-4o) e persiste em SQLite. O **dashboard** consome métricas e autentica contra a api.

## Tecnologias

| Camada | Stack |
|---|---|
| API | .NET 8, ASP.NET Core, EF Core (SQLite), SignalR, Swagger |
| Cortex | Python 3.11, FastAPI, pydub/ffmpeg, OpenCV |
| Dashboard | Python 3.9, Dash/Plotly, Flask, gunicorn |
| Frontend | HTML/CSS/JS vanilla (ES Modules), servido pela api |
| IA | Azure Speech-to-Text, Azure OpenAI (GPT-4o/Whisper), Azure Text Analytics |
| Infra | Docker, Google Cloud Run |

## Configuração (segredos)

Nenhuma chave é versionada. Configure as chaves Azure por ambiente:

- **Dev (api):** crie `services/api/appsettings.Local.json` (já no `.gitignore`) com as chaves.
- **Prod / contêiner:** injete via variáveis de ambiente / Secret Manager.
- Veja **`.env.example`** (na raiz) para a lista completa: chaves Azure, `DB_PATH`, `MediaProcessor__BaseUrl`, `BACKEND_AUTH_URL`, etc.

## Executando localmente

Com Docker (sobe os 3 serviços):

```bash
docker compose up --build
```

Ou individualmente (ordem recomendada: **cortex → api → dashboard**):

```bash
# Cortex (FastAPI)  -> http://localhost:8000
cd services/cortex && pip install -r requirements.txt && uvicorn main:app --reload

# API (.NET)        -> http://localhost:5252
cd services/api && dotnet run

# Dashboard (Dash)  -> http://localhost:8051
cd services/dashboard && pip install -r requirements.txt && gunicorn app_dashboard:server
```

## Endpoints principais da API

| Endpoint | Método | Descrição |
|---|---|---|
| `/api/transcrever` | POST | Transcrição de áudio (Azure Speech/Whisper) |
| `/api/analisar/imagem` | POST | Análise de imagens (GPT-4o Vision) |
| `/api/analisar/video` | POST | Análise de vídeos |
| `/api/analisar/oitiva` | POST | Análise de oitiva |
| `/api/analisar/laudo` | POST | Gerar laudo técnico pericial |
| `/api/analises` | GET | Listar análises salvas |
| `/api/health` | GET | Health check |

## Deploy (Google Cloud Run)

Pelos scripts em `deploy/` (ancorados na raiz do repositório):

```powershell
./deploy/deploy.ps1                 # produção (sentinel-nstech)
./deploy/deploy_test.ps1            # sandbox (sentinel-nstech-test)
./deploy/deploy.ps1 -DeployCortex   # inclui o microsserviço cortex
```

As chaves Azure devem ser injetadas no Cloud Run via `--set-env-vars` ou na configuração do serviço (ver `.env.example`).

**URL de produção:** https://sentinel-nstech-557004456190.us-central1.run.app

## Regras Anti-Alucinação

1. **Fatos apenas** — só informações explícitas na entrada.
2. **Sem suposições** — "Não informado" para dados ausentes.
3. **Neutralidade** — não acusa sem evidência.
4. **Confiança explícita** — indica nível de certeza (Alto/Médio/Baixo).

## Documentação

- `docs/PLANO_MELHORIA_HANDOFF.md` — plano de refatoração e itens pendentes.
- `docs/ARQUITETURA.md`, `docs/doc-iso/` — arquitetura e documentação de qualidade.

## Autor

**Lucas Felipe** — lucas.lfa.sc@gmail.com

*Desenvolvido para Opentech/nstech.*
