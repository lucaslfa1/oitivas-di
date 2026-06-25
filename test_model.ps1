$apiKey = "AIzaSyCFtDlr6-HwkjpdDAiSMyNDX6lrFaZhgZE"
$model = "gemini-2.5-flash"
$url = "https://generativelanguage.googleapis.com/v1beta/models/$($model):generateContent?key=$apiKey"
$body = @{
    contents = @(
        @{
            parts = @(
                @{ text = "Hello" }
            )
        }
    )
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
    Write-Host "Model $model exists!"
    $response | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Model $model failed: $_"
    $_.Exception.Response.GetResponseStream() | %{ $_.ReadToEnd() }
}

$model = "gemini-1.5-flash"
$url = "https://generativelanguage.googleapis.com/v1beta/models/$($model):generateContent?key=$apiKey"
try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
    Write-Host "Model $model exists!"
} catch {
     Write-Host "Model $model failed: $_"
}
