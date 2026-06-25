$apiKey = $env:GEMINI_API_KEY
$models = @("gemini-1.5-flash", "gemini-1.5-pro", "gemini-2.0-flash-exp")

$body = @{
    contents         = @(
        @{
            parts = @(
                @{ text = "Transcreva esta frase curta para teste de velocidade: O rápido raposa marrom pula sobre o cão preguiçoso." }
            )
        }
    )
    generationConfig = @{
        maxOutputTokens = 100
    }
} | ConvertTo-Json

Write-Host "Iniciando Benchmark de Modelos Gemini..." -ForegroundColor Cyan

foreach ($model in $models) {
    $url = "https://generativelanguage.googleapis.com/v1beta/models/$($model):generateContent?key=$apiKey"
    
    Write-Host "`nTestando $model..." -NoNewline
    
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        $sw.Stop()
        
        Write-Host " OK ($($sw.ElapsedMilliseconds)ms)" -ForegroundColor Green
        # Write-Host "Resposta: $($response.candidates[0].content.parts[0].text.Trim())" -ForegroundColor Gray
    }
    catch {
        Write-Host " FALHOU" -ForegroundColor Red
        Write-Host "Erro: $($_.Exception.Message)"
        # $_.Exception.Response.GetResponseStream() | %{ $_.ReadToEnd() }
    }
}
