# Script de Deploy com Cache-Busting Automatico (PRODUCAO)
# Incrementa a versao dos assets, commita/empurra e faz deploy no Cloud Run.
# Este script vive em deploy/ e e ancorado na raiz do repositorio.

param(
    [switch]$SkipBuild,
    [switch]$DryRun,
    [switch]$DeployCortex
)

# Ancorar na raiz do repositorio (deploy/ -> raiz), para que os caminhos
# relativos funcionem independentemente de onde o script foi invocado.
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

Write-Host "Sentinel Deploy Script" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

# 1. Incrementar versao no index.html (cache-busting dos assets do frontend)
$indexPath = "services\api\wwwroot\index.html"
$content = Get-Content $indexPath -Raw

if ($content -match 'app\.js\?v=(\d+)') {
    $currentVersion = [int]$matches[1]
    $newVersion = $currentVersion + 1

    Write-Host "Versao atual: v=$currentVersion" -ForegroundColor Yellow
    Write-Host "Nova versao:  v=$newVersion" -ForegroundColor Green

    $newContent = $content -replace 'app\.js\?v=\d+', "app.js?v=$newVersion"

    if (-not $DryRun) {
        Set-Content $indexPath -Value $newContent -NoNewline
        Write-Host "index.html atualizado" -ForegroundColor Green
    }
    else {
        Write-Host "[DRY RUN] Nao salvou alteracoes" -ForegroundColor Magenta
    }
}
else {
    Write-Host "Versao nao encontrada no index.html" -ForegroundColor Yellow
}

# 2. Git commit e push
if (-not $DryRun) {
    Write-Host ""
    Write-Host "Commitando alteracoes..." -ForegroundColor Cyan

    git add -A
    $commitMessage = "chore: bump version to v$newVersion for cache-busting"
    git commit -m $commitMessage

    Write-Host "Enviando para origin/main..." -ForegroundColor Cyan
    git push origin main

    Write-Host "Push concluido" -ForegroundColor Green
}

# 3. Deploy no Cloud Run
if (-not $SkipBuild -and -not $DryRun) {
    # 3a. Deploy sentinel-cortex (Python) se solicitado
    if ($DeployCortex) {
        Write-Host ""
        Write-Host "Deploying sentinel-cortex (Python)..." -ForegroundColor Cyan

        Set-Location (Join-Path $RepoRoot 'services\cortex')
        gcloud run deploy sentinel-cortex --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --cpu 1 --timeout 300s
        Set-Location $RepoRoot

        Write-Host "sentinel-cortex deploy concluido" -ForegroundColor Green
    }

    # 3b. Deploy sentinel-nstech (Backend .NET)
    Write-Host ""
    Write-Host "Deploying sentinel-nstech (Backend .NET)..." -ForegroundColor Cyan

    # NOTA: as chaves Azure (Speech / OpenAI / Text Analytics) devem ser injetadas via
    # --set-env-vars ou na configuracao do Cloud Run. Ver .env.example para a lista completa.
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
    gcloud run deploy sentinel-nstech --source . --region us-central1 --project sinistroia --allow-unauthenticated --memory 2Gi --timeout 900s --set-env-vars $envVars
    Set-Location $RepoRoot

    Write-Host ""
    Write-Host "Deploy concluido!" -ForegroundColor Green
    Write-Host "URL: https://sentinel-nstech-557004456190.us-central1.run.app" -ForegroundColor Cyan
}
elseif ($SkipBuild) {
    Write-Host ""
    Write-Host "Deploy pulado (--SkipBuild)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Script finalizado!" -ForegroundColor Green
