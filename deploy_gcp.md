# Guia de Deploy - Google Cloud Run

Este guia descreve como implantar o **Sentinel** no Google Cloud Run, uma plataforma serverless ideal para contêineres.

## Pré-requisitos

1.  **Google Cloud SDK (gcloud)** instalado e autenticado.
2.  Projeto no Google Cloud criado e com faturamento ativado.
3.  APIs ativadas: `Cloud Run API`, `Container Registry API` (ou `Artifact Registry`).

## Passos para Deploy

### 1. Autenticar e Configurar Projeto

```powershell
gcloud auth login
gcloud config set project [SEU_PROJECT_ID]
```

### 2. Construir e Enviar a Imagem Docker

Na raiz do projeto (`d:\sentinel-open`):

```powershell
# Substitua [PROJECT_ID] pelo ID do seu projeto
gcloud builds submit --tag gcr.io/[PROJECT_ID]/sinistro-ia
```

### 3. Implantar no Cloud Run

```powershell
gcloud run deploy sinistro-ia `
  --image gcr.io/[PROJECT_ID]/sinistro-ia `
  --platform managed `
  --region us-central1 `
  --allow-unauthenticated `
  --set-env-vars "GEMINI_API_KEY=[SUA_CHAVE_GEMINI]"
```

*   Substitua `[SUA_CHAVE_GEMINI]` pela chave que você gerou no Google AI Studio.
*   `--allow-unauthenticated` permite acesso público (ideal para web apps).

## Notas Importantes

*   **Banco de Dados:** O SQLite (`sinistros.db`) será recriado a cada reinicialização do contêiner. Para persistência real em produção, considere usar o **Cloud SQL** ou montar um volume no Cloud Run (recurso mais recente).
*   **Frontend:** O frontend estático é servido automaticamente pelo backend na porta 8080.

