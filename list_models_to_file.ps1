$apiKey = $env:GEMINI_API_KEY
$url = "https://generativelanguage.googleapis.com/v1beta/models?key=$apiKey"

try {
    $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    $response.models | ConvertTo-Json -Depth 5 | Out-File -FilePath "d:\sentinel-open\models_full.json" -Encoding UTF8
    Write-Host "Saved to d:\sentinel-open\models_full.json"
}
catch {
    Write-Host "Error listing models: $($_.Exception.Message)"
}
