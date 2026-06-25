# =============================================================================
# Sentinel - Setup Infraestrutura GCP
# Migração: Upload GCS + Job Assíncrono (v5 Production Ready)
# =============================================================================
#
# INSTRUÇÕES:
# 1. Configure as variáveis abaixo com seus valores
# 2. Certifique-se de ter o gcloud CLI instalado e autenticado
# 3. Execute: .\setup_gcp.ps1
#
# =============================================================================

# ===================== CONFIGURAÇÃO - EDITE AQUI =====================
$PROJECT_ID = "Sentinel"                 # ID do seu projeto GCP
$REGION = "southamerica-east1"            # Região principal
$BUCKET_NAME = "$PROJECT_ID-Sentinel-uploads"  # Nome único do bucket (prefixado com project ID)
$FIRESTORE_DATABASE = "(default)"         # Nome do database Firestore

# Service Accounts
$SA_API_PUBLIC = "sa-Sentinel-api"
$SA_WORKER = "sa-Sentinel-worker"
$SA_TASKS_INVOKER = "sa-cloudtasks-invoker"
$SA_EVENTARC_TRIGGER = "sa-eventarc-trigger"

# Cloud Run Services (serão criados no deploy)
$SERVICE_API_PUBLIC = "Sentinel-api-public"
$SERVICE_WORKER = "Sentinel-worker-private"

# Cloud Tasks Queue
$QUEUE_NAME = "processing-queue"
# =====================================================================

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Sentinel - Setup Infraestrutura GCP" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Verificar se gcloud está instalado
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Host "ERRO: gcloud CLI não encontrado. Instale em: https://cloud.google.com/sdk/docs/install" -ForegroundColor Red
    exit 1
}

# Configurar projeto
Write-Host "[1/10] Configurando projeto: $PROJECT_ID" -ForegroundColor Yellow
gcloud config set project $PROJECT_ID

# Habilitar APIs necessárias
Write-Host "[2/10] Habilitando APIs necessárias..." -ForegroundColor Yellow
$apis = @(
    "run.googleapis.com",
    "storage.googleapis.com",
    "firestore.googleapis.com",
    "cloudtasks.googleapis.com",
    "eventarc.googleapis.com",
    "iamcredentials.googleapis.com",
    "cloudbuild.googleapis.com",
    "artifactregistry.googleapis.com"
)
foreach ($api in $apis) {
    Write-Host "  - $api"
    gcloud services enable $api --quiet
}

# Criar Service Accounts
Write-Host "[3/10] Criando Service Accounts..." -ForegroundColor Yellow

# SA API Public
Write-Host "  - $SA_API_PUBLIC"
gcloud iam service-accounts create $SA_API_PUBLIC `
    --display-name="Sentinel API Public" `
    --quiet 2>$null

# SA Worker Private
Write-Host "  - $SA_WORKER"
gcloud iam service-accounts create $SA_WORKER `
    --display-name="Sentinel Worker Private" `
    --quiet 2>$null

# SA Cloud Tasks Invoker
Write-Host "  - $SA_TASKS_INVOKER"
gcloud iam service-accounts create $SA_TASKS_INVOKER `
    --display-name="Cloud Tasks Invoker" `
    --quiet 2>$null

# SA Eventarc Trigger
Write-Host "  - $SA_EVENTARC_TRIGGER"
gcloud iam service-accounts create $SA_EVENTARC_TRIGGER `
    --display-name="Eventarc Trigger" `
    --quiet 2>$null

# Criar Bucket GCS
Write-Host "[4/10] Criando bucket GCS: $BUCKET_NAME" -ForegroundColor Yellow
gcloud storage buckets create "gs://$BUCKET_NAME" `
    --location=$REGION `
    --uniform-bucket-level-access `
    --quiet 2>$null

# Configurar Lifecycle (delete após 24h)
Write-Host "  - Configurando lifecycle (24h retention)..."
$lifecycleJson = @"
{
  "rule": [{
    "action": {"type": "Delete"},
    "condition": {"age": 1}
  }]
}
"@
$lifecycleFile = "$env:TEMP\lifecycle.json"
$lifecycleJson | Out-File -FilePath $lifecycleFile -Encoding utf8
gcloud storage buckets update "gs://$BUCKET_NAME" --lifecycle-file=$lifecycleFile --quiet
Remove-Item $lifecycleFile

# Configurar CORS (DEV: localhost | PROD: substituir pelo domínio real)
Write-Host "  - Configurando CORS (DEV mode - restringir após deploy)..."
$corsJson = @"
[{
  "origin": ["http://localhost:5150", "http://localhost:3000"],
  "method": ["PUT", "GET"],
  "responseHeader": ["Content-Type"],
  "maxAgeSeconds": 3600
}]
"@
$corsFile = "$env:TEMP\cors.json"
$corsJson | Out-File -FilePath $corsFile -Encoding utf8
gcloud storage buckets update "gs://$BUCKET_NAME" --cors-file=$corsFile --quiet
Remove-Item $corsFile

# Criar Firestore Database (Native mode)
Write-Host "[5/10] Verificando Firestore..." -ForegroundColor Yellow
Write-Host "  NOTA: Se Firestore não estiver criado, crie manualmente no Console GCP (modo Native, região $REGION)"

# Criar Cloud Tasks Queue
Write-Host "[6/10] Criando Cloud Tasks Queue: $QUEUE_NAME" -ForegroundColor Yellow
gcloud tasks queues create $QUEUE_NAME `
    --location=$REGION `
    --max-attempts=3 `
    --max-retry-duration=3600s `
    --min-backoff=10s `
    --max-backoff=300s `
    --quiet 2>$null

# Atribuir IAM Roles
Write-Host "[7/10] Atribuindo IAM Roles..." -ForegroundColor Yellow

$SA_API_EMAIL = "$SA_API_PUBLIC@$PROJECT_ID.iam.gserviceaccount.com"
$SA_WORKER_EMAIL = "$SA_WORKER@$PROJECT_ID.iam.gserviceaccount.com"
$SA_INVOKER_EMAIL = "$SA_TASKS_INVOKER@$PROJECT_ID.iam.gserviceaccount.com"
$SA_TRIGGER_EMAIL = "$SA_EVENTARC_TRIGGER@$PROJECT_ID.iam.gserviceaccount.com"

# API Public: Token Creator (para assinar URLs) + Firestore User
Write-Host "  - $SA_API_PUBLIC: Token Creator, Firestore User"
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:$SA_API_EMAIL" `
    --role="roles/iam.serviceAccountTokenCreator" `
    --quiet 2>$null
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:$SA_API_EMAIL" `
    --role="roles/datastore.user" `
    --quiet 2>$null

# Worker: Storage Admin, Firestore User, Tasks Enqueuer
Write-Host "  - $SA_WORKER: Storage Admin, Firestore User, Tasks Enqueuer"
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:$SA_WORKER_EMAIL" `
    --role="roles/storage.objectAdmin" `
    --quiet 2>$null
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:$SA_WORKER_EMAIL" `
    --role="roles/datastore.user" `
    --quiet 2>$null
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:$SA_WORKER_EMAIL" `
    --role="roles/cloudtasks.enqueuer" `
    --quiet 2>$null

# Cloud Tasks Invoker: Worker precisa de actAs para criar tasks com OIDC
Write-Host "  - $SA_TASKS_INVOKER: Configuring actAs permission for worker"
gcloud iam service-accounts add-iam-policy-binding $SA_INVOKER_EMAIL `
    --member="serviceAccount:$SA_WORKER_EMAIL" `
    --role="roles/iam.serviceAccountUser" `
    --quiet 2>$null

# Eventarc Trigger: Configurar após deploy do Worker

# Run invoker será configurado após deploy (precisa do serviço existir)
Write-Host "  - Cloud Run Invoker: Será configurado após deploy do Worker"

# Permissão para Eventarc usar a SA de trigger
Write-Host "[8/10] Configurando Eventarc permissions..." -ForegroundColor Yellow
$PROJECT_NUMBER = (gcloud projects describe $PROJECT_ID --format="value(projectNumber)")
gcloud projects add-iam-policy-binding $PROJECT_ID `
    --member="serviceAccount:service-${PROJECT_NUMBER}@gcp-sa-eventarc.iam.gserviceaccount.com" `
    --role="roles/eventarc.eventReceiver" `
    --quiet 2>$null

# Instruções finais
Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "SETUP CONCLUÍDO!" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "PRÓXIMOS PASSOS:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. FIRESTORE: Se ainda não existe, crie no Console GCP:" -ForegroundColor White
Write-Host "   https://console.cloud.google.com/firestore/databases?project=$PROJECT_ID"
Write-Host "   - Modo: Native"
Write-Host "   - Região: $REGION"
Write-Host ""
Write-Host "2. DEPLOY DOS SERVIÇOS (após implementar o código):" -ForegroundColor White
Write-Host ""
Write-Host "   # API Public (permite acesso não autenticado)" -ForegroundColor Cyan
Write-Host "   gcloud run deploy $SERVICE_API_PUBLIC \" -ForegroundColor Cyan
Write-Host "       --source . \" -ForegroundColor Cyan
Write-Host "       --region $REGION \" -ForegroundColor Cyan
Write-Host "       --allow-unauthenticated \" -ForegroundColor Cyan
Write-Host "       --service-account=$SA_API_EMAIL" -ForegroundColor Cyan
Write-Host ""
Write-Host "   # Worker Private (apenas chamadas autenticadas)" -ForegroundColor Cyan
Write-Host "   gcloud run deploy $SERVICE_WORKER \" -ForegroundColor Cyan
Write-Host "       --source . \" -ForegroundColor Cyan
Write-Host "       --region $REGION \" -ForegroundColor Cyan
Write-Host "       --no-allow-unauthenticated \" -ForegroundColor Cyan
Write-Host "       --service-account=$SA_WORKER_EMAIL \" -ForegroundColor Cyan
Write-Host "       --concurrency=1 \" -ForegroundColor Cyan
Write-Host "       --memory=2Gi \" -ForegroundColor Cyan
Write-Host "       --timeout=29m" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. APÓS DEPLOY DO WORKER, configure permissões de invocação:" -ForegroundColor White
Write-Host ""
Write-Host "   # Cloud Tasks Invoker" -ForegroundColor Cyan
Write-Host "   gcloud run services add-iam-policy-binding $SERVICE_WORKER \" -ForegroundColor Cyan
Write-Host "       --region=$REGION \" -ForegroundColor Cyan
Write-Host "       --member=serviceAccount:$SA_INVOKER_EMAIL \" -ForegroundColor Cyan
Write-Host "       --role=roles/run.invoker" -ForegroundColor Cyan
Write-Host ""
Write-Host "   # Eventarc Trigger" -ForegroundColor Cyan
Write-Host "   gcloud run services add-iam-policy-binding $SERVICE_WORKER \" -ForegroundColor Cyan
Write-Host "       --region=$REGION \" -ForegroundColor Cyan
Write-Host "       --member=serviceAccount:$SA_TRIGGER_EMAIL \" -ForegroundColor Cyan
Write-Host "       --role=roles/run.invoker" -ForegroundColor Cyan
Write-Host ""
Write-Host "4. CRIAR EVENTARC TRIGGER:" -ForegroundColor White
Write-Host ""
Write-Host "   gcloud eventarc triggers create gcs-upload-trigger \" -ForegroundColor Cyan
Write-Host "       --location=$REGION \" -ForegroundColor Cyan
Write-Host "       --destination-run-service=$SERVICE_WORKER \" -ForegroundColor Cyan
Write-Host "       --destination-run-path=/api/events/gcs-finalize \" -ForegroundColor Cyan
Write-Host "       --event-filters=type=google.cloud.storage.object.v1.finalized \" -ForegroundColor Cyan
Write-Host "       --event-filters=bucket=$BUCKET_NAME \" -ForegroundColor Cyan
Write-Host "       --service-account=$SA_TRIGGER_EMAIL" -ForegroundColor Cyan
Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Variáveis para appsettings.json:" -ForegroundColor Yellow
Write-Host @"
{
  "GCS": {
    "BucketName": "$BUCKET_NAME",
    "ProjectId": "$PROJECT_ID",
    "SignedUrlExpirationMinutes": 15
  },
  "Firestore": {
    "ProjectId": "$PROJECT_ID",
    "JobsCollection": "processing_jobs"
  },
  "Worker": {
    "WorkerServiceUrl": "https://$SERVICE_WORKER-xxxxx-rj.a.run.app",
    "CloudTasksQueue": "projects/$PROJECT_ID/locations/$REGION/queues/$QUEUE_NAME",
    "ServiceAccountEmail": "$SA_INVOKER_EMAIL"
  }
}
"@ -ForegroundColor Gray

