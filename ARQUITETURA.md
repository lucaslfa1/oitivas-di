# Sentinel - Arquitetura do Sistema

## Visao Geral

**Sentinel** é um sistema de análise forense de sinistros veiculares que utiliza Inteligência Artificial para automatizar a transcrição e análise de oitivas, vídeos e imagens.

---

## Tecnologias Utilizadas

| Camada | Tecnologia | Finalidade |
|--------|------------|------------|
| **Frontend** | HTML5 + CSS3 + JavaScript | Interface responsiva |
| **Backend** | ASP.NET Core 8 (C#) | API REST |
| **IA - Transcricao** | Google Gemini 2.0 Flash | Transcrição de áudio |
| **IA - Analise** | Azure OpenAI GPT-4 | Geração de laudos periciais |
| **IA - Video/Imagem** | Google Gemini 2.0 Flash | Análise visual |
| **Banco de Dados** | SQLite | Persistência local |
| **Hospedagem** | Microsoft Azure Web App | Deploy em produção |

---

## Estrutura de Pastas

```
Sentinel/
├── Backend/
│   ├── Program.cs                    # Ponto de entrada + Rotas da API
│   ├── appsettings.json              # Configurações (API Keys)
│   ├── SinistroAPI.csproj            # Projeto .NET
│   │
│   ├── Services/                     # Camada de Servicos (IA)
│   │   ├── GeminiTranscricaoService.cs   # Transcrição de áudio (Gemini)
│   │   ├── GeminiFileApiService.cs       # Upload de arquivos grandes (File API)
│   │   ├── AzureOpenAIService.cs         # Geração de laudos (GPT-4)
│   │   ├── ImagemAnaliseService.cs       # Análise de imagens
│   │   ├── VideoAnaliseService.cs        # Análise de vídeos
│   │   └── DescricaoAnaliseService.cs    # Análise de oitivas
│   │
│   ├── Prompts/                      # Prompts de IA
│   │   └── SinistroPrompts.cs            # Templates de prompts especializados
│   │
│   ├── Data/                         # Camada de Dados
│   │   └── AppDbContext.cs               # Entity Framework (SQLite)
│   │
│   ├── Models/                       # Modelos de Dados
│   │   └── AnaliseModel.cs               # Modelo de análise salva
│   │
│   └── wwwroot/                      # Frontend (SPA)
│       ├── index.html                    # Página principal
│       ├── style.css                     # Estilos globais
│       └── js/
│           ├── main.js                   # Inicialização
│           ├── config/constants.js       # Configurações
│           ├── services/
│           │   ├── sinistroApi.js        # Chamadas à API
│           │   ├── analise.js            # Lógica de análise
│           │   └── export.js             # Exportação PDF
│           └── ui/
│               ├── modal.js              # Modais
│               └── user.js               # Interface do usuário
```

---

## Fluxo de Dados

```
+------------------+     +------------------+     +------------------+
|    FRONTEND      |---->|    BACKEND       |---->|   SERVICOS IA    |
|   (JavaScript)   |     |  (ASP.NET Core)  |     | (Gemini/Azure)   |
+------------------+     +------------------+     +------------------+
        |                       |                       |
        |   Upload de Audio     |   File API Upload     |
        | --------------------->| --------------------->|
        |                       |                       |
        |                       |   Transcricao         |
        |   Texto Transcrito    |<--------------------- |
        |<--------------------- |                       |
        |                       |                       |
        |   Gerar Laudo         |   Prompt + Contexto   |
        | --------------------->| --------------------->|
        |                       |                       |
        |   Laudo em Markdown   |   Resposta GPT-4      |
        |<--------------------- |<--------------------- |
```

---

## Endpoints da API

| Metodo | Rota | Descricao | Servico |
|--------|------|-----------|---------|
| `POST` | `/api/transcrever` | Transcrição de áudio | Gemini |
| `POST` | `/api/analisar/laudo` | Gerar laudo pericial | Azure GPT-4 |
| `POST` | `/api/analisar/imagem` | Análise de imagens | Gemini |
| `POST` | `/api/analisar/video` | Análise de vídeos | Gemini File API |
| `POST` | `/api/analisar/oitiva` | Análise de transcrição | Gemini |
| `POST` | `/api/salvar` | Salvar análise | SQLite |
| `GET`  | `/api/analises` | Listar análises salvas | SQLite |
| `GET`  | `/api/analises/{id}` | Buscar análise por ID | SQLite |
| `DELETE` | `/api/analises/{id}` | Deletar análise | SQLite |
| `GET`  | `/api/health` | Health check | Sistema |

---

## Arquitetura de IA

### Transcricao de Audio (Gemini)
```
Audio (MP3/WAV/M4A) 
    |
    v
+------------------------------------------+
|  Arquivo > 15MB?                         |
|  |-- SIM -> File API (upload resumavel)  | <- Suporta até 2GB
|  +-- NAO -> Inline Data (base64)         | <- Rápido para pequenos
+------------------------------------------+
    |
    v
Gemini 2.0 Flash -> Transcricao formatada
```

### Geracao de Laudo (GPT-4)
```
Transcricao + Contexto + Duracao
    |
    v
+------------------------------------------+
|  Prompt Especializado                    |
|  - Análise de coerência                  |
|  - Detecção de contradições              |
|  - Identificação de pontos críticos      |
|  - Estrutura pericial formal             |
+------------------------------------------+
    |
    v
Azure OpenAI GPT-4 -> Laudo em Markdown
```

---

## Infraestrutura

```
+-------------------------------------------------------------+
|                    MICROSOFT AZURE                          |
+-------------------------------------------------------------+
|                                                             |
|  +------------------+     +-----------------------------+   |
|  |  Azure Web App   |     |  Azure OpenAI               |   |
|  |  (Linux/Win)     |---->|  (GPT-4 Deployment)         |   |
|  |  analise-        |     |  sinistro-ai-pro-resource   |   |
|  |  sinistro        |     +-----------------------------+   |
|  +--------+---------+                                       |
|           |                                                 |
+-----------|-------------------------------------------------+
            |
            | HTTPS
            v
+-------------------------------------------------------------+
|                    GOOGLE CLOUD                             |
+-------------------------------------------------------------+
|                                                             |
|  +-------------------------------------------------------+  |
|  |  Generative Language API (Gemini 2.0 Flash)           |  |
|  |  - Transcricao de audio                               |  |
|  |  - Analise de video                                   |  |
|  |  - Analise de imagem                                  |  |
|  |  - File API (uploads até 2GB)                         |  |
|  +-------------------------------------------------------+  |
|                                                             |
+-------------------------------------------------------------+
```

---

## Seguranca

- **API Keys** armazenadas em `appsettings.json` (não versionado)
- **CORS** configurado para domínios específicos em produção
- **HTTPS** forçado em produção
- **Rate Limiting** via quotas das APIs (Gemini/Azure)

---

## Limites do Sistema

| Recurso | Limite |
|---------|--------|
| Upload de audio | Até 500MB (backend) |
| Transcricao (Gemini) | Até 2GB via File API |
| Upload de video | Até 500MB (backend), 2GB (File API) |
| Timeout de requisicao | 15-30 minutos |
| Analises salvas | Ilimitado (SQLite) |

---

## Deploy

```bash
# Build para producao
dotnet publish SinistroAPI.csproj -c Release -o ./publish

# Criar ZIP
Compress-Archive -Path ./publish/* -DestinationPath ./deploy.zip -Force

# Deploy no Azure
az webapp deploy --resource-group <RG> --name analise-sinistro --src-path ./deploy.zip --type zip
```

---

## Desenvolvedor

**Lucas** - Sistema desenvolvido para análise forense de sinistros veiculares com IA.

---

*Documentacao gerada em: Dezembro 2025*

