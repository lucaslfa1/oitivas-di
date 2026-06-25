$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$model = "models/gemini-2.0-flash"

# Simulating the parallel logic flow with a simple prompt
$jobs = 1..3
$results = @()

Write-Host "Simulating Parallel Transcription with $model..."

$jobs | ForEach-Object {
    $i = $_
    $body = @{
        contents = @(
            @{
                role  = "user"
                parts = @(
                    @{ text = "Escreva o numero $i por extenso." }
                )
            }
        )
    } | ConvertTo-Json -Depth 5

    $url = "https://generativelanguage.googleapis.com/v1beta/$($model):generateContent?key=$apiKey"
    
    # In real C# code this is async, here in PS we just test the endpoint/model validty linearly to be safe
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        Write-Host "Chunk $i: $($response.candidates[0].content.parts[0].text.Trim())" -ForegroundColor Green
    }
    catch {
        Write-Host "Chunk $i Falhou: $($_.Exception.Message)" -ForegroundColor Red
    }
}
