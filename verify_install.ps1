$ErrorActionPreference = "SilentlyContinue"

function Check-Command {
    param (
        [string]$Name,
        [string]$Command,
        [string]$Args = "--version"
    )

    $process = Start-Process -FilePath $Command -ArgumentList $Args -NoNewWindow -PassThru -Wait -ErrorAction SilentlyContinue
    
    if ($?) {
        # Validating command existence by trying to run it
        try {
            $output = & $Command $Args 2>&1
            if ($LASTEXITCODE -eq 0) {
                # Clean up output to just get the version line if possible, or first line
                $version = $output | Select-Object -First 1
                Write-Host "$Name : Installed ($version)" -ForegroundColor Green
            }
            else {
                Write-Host "$Name : Error executing command" -ForegroundColor Red
            }
        }
        catch {
            Write-Host "$Name : Not Found" -ForegroundColor Red
        }
    }
    else {
        Write-Host "$Name : Not Found" -ForegroundColor Red
    }
}

Write-Host "--- Verification Report ---" -ForegroundColor Cyan

# Git
try { 
    $gitv = git --version 
    Write-Host "Git : $gitv" -ForegroundColor Green 
}
catch { Write-Host "Git : Not Found" -ForegroundColor Red }

# Node.js
try { 
    $nodev = node --version 
    Write-Host "Node.js : $nodev" -ForegroundColor Green 
}
catch { Write-Host "Node.js : Not Found" -ForegroundColor Red }

# Python
try { 
    $pyv = python --version 2>&1
    Write-Host "Python : $pyv" -ForegroundColor Green 
}
catch { Write-Host "Python : Not Found" -ForegroundColor Red }

# .NET
try {
    $dotnetv = dotnet --version
    Write-Host ".NET SDK : $dotnetv" -ForegroundColor Green
}
catch { Write-Host ".NET SDK : Not Found" -ForegroundColor Red }

# Docker
try {
    $dockerv = docker --version
    Write-Host "Docker : $dockerv" -ForegroundColor Green
}
catch { Write-Host "Docker : Not Found" -ForegroundColor Red }

# WSL
try {
    $wslstatus = wsl --status | Select-Object -First 1
    if ($wslstatus) {
        Write-Host "WSL2 : Installed ($wslstatus)" -ForegroundColor Green
    }
    else {
        Write-Host "WSL2 : Not Found or Error" -ForegroundColor Red
    }
}
catch { Write-Host "WSL2 : Not Found" -ForegroundColor Red }

# Google Cloud SDK
try {
    $gcloudv = gcloud --version | Select-Object -First 1
    Write-Host "Google Cloud SDK : $gcloudv" -ForegroundColor Green
}
catch { Write-Host "Google Cloud SDK : Not Found" -ForegroundColor Red }

# Azure CLI
try {
    $azv = az --version | Select-Object -First 1
    if ($azv) {
        Write-Host "Azure CLI : $azv" -ForegroundColor Green
    }
    else {
        Write-Host "Azure CLI : Not Found (command exists but no output?)" -ForegroundColor Yellow
    }
}
catch { Write-Host "Azure CLI : Not Found" -ForegroundColor Red }

# PostgreSQL
# try {
#     $psqlv = psql --version
#     Write-Host "PostgreSQL : $psqlv" -ForegroundColor Green
# }
# catch { Write-Host "PostgreSQL : Not Found" -ForegroundColor Red }
Write-Host "PostgreSQL : Not Needed (Using SQLite/Firestore)" -ForegroundColor Gray

# Redis
# try {
#     $redisv = redis-cli --version
#     Write-Host "Redis : $redisv" -ForegroundColor Green
# }
# catch { Write-Host "Redis : Not Found" -ForegroundColor Red }
Write-Host "Redis : Not Needed (No dependency found)" -ForegroundColor Gray


Write-Host "--- End of Report ---" -ForegroundColor Cyan
