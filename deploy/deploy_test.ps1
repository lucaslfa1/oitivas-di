# Script de Deploy para Ambiente de Testes (Sandbox)
# Faz o deploy para o servico 'sentinel-nstech-test' no Cloud Run (sem git push).
# Este script vive em deploy/ e e ancorado na raiz do repositorio.

param(
    [switch]$SkipBuild,
    [switch]$DryRun,
    [switch]$DeployCortex
)

# Ancorar na raiz do repositorio (deploy/ -> raiz).
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

Write-Host "Sentinel Deploy Script - AMBIENTE DE TESTES" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Incrementar versao no index.html (Cache-Busting)
$indexPath = "services\api\wwwroot\index.html"
$content = Get-Content $indexPath -Raw

if ($content -match 'app\.js\?v=(\d+)') {
    $currentVersion = [int]$matches[1]
    $newVersion = $currentVersion + 1

    Write-Host "Versao atual de teste: v=$currentVersion" -ForegroundColor Yellow
    Write-Host "Nova versao de teste:  v=$newVersion" -ForegroundColor Green

    $newContent = $content -replace 'app\.js\?v=\d+', "app.js?v=$newVersion"

    if (-not $DryRun) {
        Set-Content $indexPath -Value $newContent -NoNewline
        Write-Host "index.html atualizado localmente" -ForegroundColor Green
    }
}
else {
    Write-Host "Versao nao encontrada no index.html" -ForegroundColor Yellow
}

# NOTA: em testes NAO fazemos git commit/push para nao poluir o historico de producao.
Write-Host "Pulando commit e push automatico do Git (ambiente de teste)." -ForegroundColor Yellow

# 2. Deploy no Cloud Run (Servico de Teste: sentinel-nstech-test)
if (-not $SkipBuild -and -not $DryRun) {
    # 2a. Deploy sentinel-cortex (Python) se solicitado
    if ($DeployCortex) {
        Write-Host ""
        Write-Host "Deploying sentinel-cortex (Python)..." -ForegroundColor Cyan

        Set-Location (Join-Path $RepoRoot 'services\cortex')
        gcloud run deploy sentinel-cortex --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --cpu 1 --timeout 300s
        Set-Location $RepoRoot

        Write-Host "sentinel-cortex deploy concluido" -ForegroundColor Green
    }

    # 2b. Deploy sentinel-nstech-test (Backend .NET de Testes)
    Write-Host ""
    Write-Host "Deploying sentinel-nstech-test (Backend .NET)..." -ForegroundColor Cyan

    # NOTA: as chaves Azure devem ser injetadas via --set-env-vars ou config do Cloud Run (ver .env.example).
    $envVars = "ASPNETCORE_ENVIRONMENT=Production,MediaProcessor__BaseUrl=https://sentinel-cortex-557004456190.us-central1.run.app"

    # Carrega variaveis adicionais do arquivo .env (caso exista na raiz)
    $envPath = Join-Path $RepoRoot ".env"
    if (Test-Path $envPath) {
        Write-Host "Carregando chaves e configuracoes adicionais de $envPath..." -ForegroundColor Yellow
        Get-Content $envPath | ForEach-Object {
            $line = $_.Trim()
            if ($line -and -not $line.StartsWith("#") -and $line -match "^([^=]+)=(.*)$") {
                $key = $Matches[1].Trim()
                $val = $Matches[2].Trim()
                if ($val) {
                    $envVars += ",$key=$val"
                }
            }
        }
    }

    Set-Location (Join-Path $RepoRoot 'services\api')
    gcloud run deploy sentinel-nstech-test --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --timeout 900s --set-env-vars $envVars
    Set-Location $RepoRoot

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
