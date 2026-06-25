# Sentinel - Sistema de Análise Forense de Sinistros Veiculares

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Google Cloud](https://img.shields.io/badge/Google%20Cloud-Run-4285F4.svg)](https://cloud.google.com/)

## 📋 Descrição

Sistema especializado em análise forense de sinistros veiculares utilizando Inteligência Artificial para:

- 🎙️ **Transcrição de Oitivas** - Converte áudios de ligações (Operador BAS ↔ Motorista) em texto estruturado
- 🖼️ **Análise de Imagens** - Avalia danos em veículos através de fotografias
- 🎬 **Análise de Vídeos** - Processa dashcams, câmeras de segurança e depoimentos em vídeo
- 📝 **Geração de Laudos** - Produz relatórios técnicos periciais automatizados
- 🔍 **Detecção de Fraudes** - Identifica inconsistências e padrões suspeitos

## 🏗️ Arquitetura

```
Sentinel/
├── Backend/                    # API .NET 8
│   ├── Services/              # Serviços de IA
│   │   ├── GeminiTranscricaoService.cs   # Transcrição de áudio
│   │   ├── GeminiFileApiService.cs       # Upload de arquivos grandes
│   │   ├── ImagemAnaliseService.cs       # Análise de imagens
│   │   ├── VideoAnaliseService.cs        # Análise de vídeos
│   │   ├── DescricaoAnaliseService.cs    # Geração de laudos
│   │   └── VertexAIService.cs            # Integração Vertex AI (futuro)
│   ├── Configuration/         # Configurações da aplicação
│   ├── Models/                # DTOs e entidades
│   └── Data/                  # Contexto do banco (SQLite)
│
└── Frontend/                   # Interface Web (wwwroot/)
    ├── js/                    # JavaScript modular
    │   ├── api/               # Comunicação com API
    │   ├── features/          # Funcionalidades
    │   └── ui/                # Componentes de interface
    └── css/                   # CSS modular
```


## 🚀 Tecnologias

### Backend
- **.NET 8** - Framework principal
- **Entity Framework Core** - ORM com SQLite
- **Google Gemini API** - IA generativa multimodal (transcrição, análise de imagem/vídeo, geração de laudos)

### Frontend
- **HTML5/CSS3/JavaScript** - Stack vanilla
- **Marked.js** - Renderização de Markdown
- **html2pdf.js** - Exportação de PDFs
- **Design responsivo** - Mobile-first

### Infraestrutura
- **Google Cloud Run** - Hospedagem (containerizado)
- **Google Container Registry** - Registry de imagens Docker
- **SQLite** - Banco de dados

## 📡 Endpoints da API

| Endpoint | Método | Descrição |
|----------|--------|-----------|
| `/api/transcrever` | POST | Transcrição de áudio (Gemini) |
| `/api/analisar/imagem` | POST | Análise de imagens |
| `/api/analisar/video` | POST | Análise de vídeos |
| `/api/analisar/oitiva` | POST | Análise de oitiva |
| `/api/analisar/laudo` | POST | Gerar laudo técnico pericial |
| `/api/salvar` | POST | Salvar análise no banco |
| `/api/analises` | GET | Listar análises salvas |
| `/api/analises/{id}` | GET/DELETE | Buscar/deletar análise |
| `/api/health` | GET | Health check |

## 🚀 Deploy

### Google Cloud Run

```bash
# Build da imagem
gcloud builds submit --tag gcr.io/PROJECT_ID/sinistro-api:latest

# Deploy
gcloud run deploy sinistro-api \
  --image gcr.io/PROJECT_ID/sinistro-api:latest \
  --platform managed \
  --region us-central1 \
  --allow-unauthenticated \
  --set-env-vars "GEMINI_API_KEY=SUA_CHAVE,DB_PATH=/data/sinistros.db" \
  --memory 1Gi \
  --timeout 600
```

### Local

```bash
cd Backend
dotnet run
# Acesse http://localhost:5252
```

## 🛡️ Regras Anti-Alucinação

O sistema implementa proteções rigorosas contra alucinações de IA:

1. **Fatos apenas** - Apenas informações explícitas na entrada
2. **Sem suposições** - "Não informado" para dados ausentes
3. **Neutralidade** - Não acusa sem evidência
4. **Confiança explícita** - Indica nível de certeza (Alto/Médio/Baixo)

## 🔐 Segurança

- CORS configurado por ambiente
- API keys em variáveis de ambiente (não no código)
- Validação de entrada
- Logs estruturados

## 🌐 URL de Produção

**Cloud Run:** https://sinistro-api-557004456190.us-central1.run.app

## 👤 Autor

**Lucas Felipe** - [lucas.lfa@live.com](mailto:lucas.lfa@live.com)

---

*Desenvolvido para Opentech/nstech*

