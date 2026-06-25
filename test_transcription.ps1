$apiKey = $env:GEMINI_API_KEY
$model = "models/gemini-2.0-flash"
$audioPath = "d:\sentinel-open\part1.wav"

if (-not (Test-Path $audioPath)) {
    Write-Error "Audio file not found: $audioPath"
    exit
}

$audioBytes = [System.IO.File]::ReadAllBytes($audioPath)
$base64Audio = [Convert]::ToBase64String($audioBytes)

$body = @{
    contents         = @(
        @{
            role  = "user"
            parts = @(
                @{ text = "Transcreva este áudio para português." },
                @{
                    inline_data = @{
                        mime_type = "audio/wav"
                        data      = $base64Audio
                    }
                }
            )
        }
    )
    generationConfig = @{
        maxOutputTokens = 8192
        temperature     = 0.0
    }
} | ConvertTo-Json -Depth 5

$url = "https://generativelanguage.googleapis.com/v1beta/$($model):generateContent?key=$apiKey"

Write-Host "Enviando áudio para $model..."
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
    $sw.Stop()
    
    Write-Host "Sucesso! Tempo: $($sw.ElapsedMilliseconds)ms" -ForegroundColor Green
    $text = $response.candidates[0].content.parts[0].text
    Write-Host "Transcrição:" -ForegroundColor Cyan
    Write-Host $text
}
catch {
    Write-Host "FALHOU" -ForegroundColor Red
    Write-Host "Status: $($_.Exception.Response.StatusCode)"
    $stream = $_.Exception.Response.GetResponseStream()
    if ($stream) {
        $reader = New-Object System.IO.StreamReader($stream)
        Write-Host "Erro detalhado: $($reader.ReadToEnd())"
    }
}
