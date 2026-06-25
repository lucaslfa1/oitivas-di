$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$model = "models/gemini-2.0-flash"

$jobs = 1..3
Write-Host "Simulating Parallel Transcription with $model..."

foreach ($i in $jobs) {
    $bodyObject = @{
        contents = @(
            @{
                role  = "user"
                parts = @(
                    @{ text = "Escreva o numero $i por extenso. Apenas o texto." }
                )
            }
        )
    }
    $body = $bodyObject | ConvertTo-Json -Depth 5

    $url = "https://generativelanguage.googleapis.com/v1beta/$($model):generateContent?key=$apiKey"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        
        # Simplify access
        $candidates = $response.candidates
        if ($candidates) {
            $content = $candidates[0].content
            $parts = $content.parts
            $text = $parts[0].text
            $text = $text.Trim()
            Write-Host "Chunk $i: $text" -ForegroundColor Green
        }
        else {
            Write-Host "Chunk $i: Sem candidatos" -ForegroundColor Yellow
        }

    }
    catch {
        Write-Host "Chunk $i Falhou: $($_.Exception.Message)" -ForegroundColor Red
    }
}
