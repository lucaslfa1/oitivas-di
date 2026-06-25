$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$models = @("gemini-2.5-flash", "gemini-2.0-flash", "models/gemini-flash-latest")

$body = @{
    contents = @(
        @{
            role  = "user"
            parts = @(
                @{ text = "Hello" }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Write-Host "JSON Body: $body"

foreach ($model in $models) {
    if ($model -notmatch "^models/") { $modelName = "models/$model" } else { $modelName = $model }
    
    $url = "https://generativelanguage.googleapis.com/v1beta/$($modelName):generateContent?key=$apiKey"
    
    Write-Host "`nTestando $modelName..." -NoNewline
    
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        $sw.Stop()
        
        Write-Host " OK ($($sw.ElapsedMilliseconds)ms)" -ForegroundColor Green
        # Write-Host "Response: $($response.candidates[0].content.parts[0].text)"
    }
    catch {
        Write-Host " FALHOU" -ForegroundColor Red
        Write-Host "Status: $($_.Exception.Response.StatusCode)"
        $stream = $_.Exception.Response.GetResponseStream()
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            $errBody = $reader.ReadToEnd()
            Write-Host "Body: $errBody"
        }
    }
}
