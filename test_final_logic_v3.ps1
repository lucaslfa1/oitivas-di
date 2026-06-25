$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$model = "models/gemini-2.0-flash"

$jobs = 1..3
Write-Host "Simulating Parallel Transcription with $model..."

foreach ($i in $jobs) {
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
        $text = $response.candidates[0].content.parts[0].text.Trim()
        Write-Host "Chunk $i: $text" -ForegroundColor Green
    }
    catch {
        Write-Host "Chunk $i Falhou: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                Write-Host "Detalhe: $($reader.ReadToEnd())"
            }
        }
    }
}
