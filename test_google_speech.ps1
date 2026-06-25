$apiUrl = "http://localhost:5150/api/transcrever"
$audioPath = "part1.wav" # Certifique-se que existe ou ajuste o caminho

if (-not (Test-Path $audioPath)) {
    Write-Warning "Arquivo de áudio não encontrado em $audioPath. Tentando encontrar um .wav qualquer."
    $audioPath = Get-ChildItem -Filter *.wav | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $audioPath) {
    Write-Error "Nenhum arquivo .wav encontrado para teste."
    exit
}

Write-Host "Testando transcrição com Google Cloud Speech..." -ForegroundColor Cyan
Write-Host "Arquivo: $audioPath"
Write-Host "URL: $apiUrl"

$boundary = [System.Guid]::NewGuid().ToString()
$LF = "`r`n"

$fileBytes = [System.IO.File]::ReadAllBytes($audioPath)
$fileEnc = [System.Text.Encoding]::GetEncoding("iso-8859-1")

$bodyLines = (
    "--$boundary",
    "Content-Disposition: form-data; name=`"Arquivo`"; filename=`"$([System.IO.Path]::GetFileName($audioPath))`"",
    "Content-Type: audio/wav",
    "",
    $fileEnc.GetString($fileBytes),
    "--$boundary--$LF"
) -join $LF

try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method Post -ContentType "multipart/form-data; boundary=$boundary" -Body $bodyLines -ErrorAction Stop
    
    Write-Host "Status: SUCESSO" -ForegroundColor Green
    Write-Host "Provedor: $($response.provider)" -ForegroundColor Yellow
    Write-Host "Texto Transcrito:" -ForegroundColor White
    Write-Host $response.transcricao
}
catch {
    Write-Host "ERRO NA REQUISIÇÃO" -ForegroundColor Red
    Write-Host $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host $reader.ReadToEnd()
    }
}
