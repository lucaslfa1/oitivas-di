# Script para iniciar o Python Media Processor
Write-Host "Iniciando Sentinel Python Processor..." -ForegroundColor Cyan

$venvPath = ".\sentinel-cortex\.venv"

# Verificar se venv existe
if (-not (Test-Path $venvPath)) {
    Write-Host "Criando ambiente virtual..."
    python -m venv $venvPath
    
    Write-Host "Instalando dependências..."
    & "$venvPath\Scripts\pip" install -r .\sentinel-cortex\requirements.txt
}

# Ativar e rodar
Write-Host "Iniciando servidor na porta 8000..."
$env:PYTHONPATH = ".\sentinel-cortex"
& "$venvPath\Scripts\python" .\sentinel-cortex\main.py

