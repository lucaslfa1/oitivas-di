# Script de Deploy para Ambiente de Testes (Sandbox)
# Realiza o deploy para o serviço 'sentinel-nstech-test' no Cloud Run

param(
    [switch]$SkipBuild,
    [switch]$DryRun,
    [switch]$DeployCortex
)

Write-Host "Sentinel Deploy Script - AMBIENTE DE TESTES" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Incrementar versao no index.html (Cache-Busting)
$indexPath = "Backend\wwwroot\index.html"
$content = Get-Content $indexPath -Raw

# Encontrar versao atual
if ($content -match 'app\.js\?v=(\d+)') {
    $currentVersion = [int]$matches[1]
    $newVersion = $currentVersion + 1
    
    Write-Host "Versao atual de teste: v=$currentVersion" -ForegroundColor Yellow
    Write-Host "Nova versao de teste:  v=$newVersion" -ForegroundColor Green
    
    # Substituir todas as ocorrencias de versao
    $newContent = $content -replace 'app\.js\?v=\d+', "app.js?v=$newVersion"
    
    if (-not $DryRun) {
        Set-Content $indexPath -Value $newContent -NoNewline
        Write-Host "index.html atualizado localmente" -ForegroundColor Green
    }
}
else {
    Write-Host "Versao nao encontrada no index.html" -ForegroundColor Yellow
}

# NOTA: Para testes, NÃO realizamos o git commit e git push automático no origin/main 
# para não poluir o histórico de commits de produção.
Write-Host "Pulando commit e push automático do Git (ambiente de teste)." -ForegroundColor Yellow

# 2. Deploy no Cloud Run (Serviço de Teste: sentinel-nstech-test)
if (-not $SkipBuild -and -not $DryRun) {
    # 2a. Deploy sentinel-cortex (Python) se solicitado
    if ($DeployCortex) {
        Write-Host ""
        Write-Host "Deploying sentinel-cortex (Python)..." -ForegroundColor Cyan
        
        Set-Location sentinel-cortex
        gcloud run deploy sentinel-cortex --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --cpu 1 --timeout 300s
        Set-Location ..
        
        Write-Host "sentinel-cortex deploy concluido" -ForegroundColor Green
    }

    # 2b. Deploy sentinel-nstech-test (Backend .NET de Testes)
    Write-Host ""
    Write-Host "Deploying sentinel-nstech-test (Backend .NET)..." -ForegroundColor Cyan
    
    $envVars = "ASPNETCORE_ENVIRONMENT=Production,MediaProcessor__BaseUrl=https://sentinel-cortex-557004456190.us-central1.run.app"
    if (-not [string]::IsNullOrEmpty($env:GEMINI_API_KEY)) {
        Write-Host "Configurando GEMINI_API_KEY no Cloud Run a partir de `$env:GEMINI_API_KEY..." -ForegroundColor Green
        $envVars += ",GEMINI_API_KEY=$($env:GEMINI_API_KEY)"
    } else {
        Write-Host "AVISO: `$env:GEMINI_API_KEY local está vazia. O deploy não atualizará a chave no Cloud Run." -ForegroundColor Yellow
    }

    Set-Location Backend
    gcloud run deploy sentinel-nstech-test --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --timeout 900s --set-env-vars $envVars
    
    Set-Location ..
    
    Write-Host ""
    Write-Host "Deploy de teste concluido!" -ForegroundColor Green
    Write-Host "URL de Teste: https://sentinel-nstech-test-557004456190.us-central1.run.app/index.html" -ForegroundColor Cyan
}
elseif ($SkipBuild) {
    Write-Host ""
    Write-Host "Deploy pulado (--SkipBuild)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Script finalizado!" -ForegroundColor Green
