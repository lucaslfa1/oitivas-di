$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$model = "models/gemini-2.0-flash"

Write-Host "Simulating Parallel Transcription with $model..."

for ($i = 1; $i -le 3; $i++) {
    $body = @{
        contents = @(
            @{
                role  = "user"
                parts = @(
                    @{ text = "Escreva o numero $i por extenso. Apenas o texto." }
                )
            }
        )
    } | ConvertTo-Json -Depth 5

    $url = "https://generativelanguage.googleapis.com/v1beta/$($model):generateContent?key=$apiKey"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        
        # Extremely safe access
        $candidates = $response | Select-Object -ExpandProperty candidates
        $content = $candidates | Select-Object -ExpandProperty content
        $parts = $content | Select-Object -ExpandProperty parts
        $text = $parts | Select-Object -ExpandProperty text
        
        Write-Host "Chunk $i: $text" -ForegroundColor Green
    }
    catch {
        Write-Host "Chunk $i Falhou: $($_.Exception.Message)" -ForegroundColor Red
    }
}
