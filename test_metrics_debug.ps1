$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$models = @("gemini-1.5-flash", "gemini-1.5-pro", "gemini-2.0-flash-exp")

$body = @{
    contents = @(
        @{
            parts = @(
                @{ text = "Hello" }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Write-Host "JSON Body: $body"

foreach ($model in $models) {
    $url = "https://generativelanguage.googleapis.com/v1beta/models/$($model):generateContent?key=$apiKey"
    
    Write-Host "`nTestando $model..." -NoNewline
    
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        $sw.Stop()
        
        Write-Host " OK ($($sw.ElapsedMilliseconds)ms)" -ForegroundColor Green
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
