$apiKey = $env:GEMINI_API_KEY
$url = "https://generativelanguage.googleapis.com/v1beta/models?key=$apiKey"

try {
    $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    $response.models | Select-Object name, displayName, supportedGenerationMethods | ConvertTo-Json -Depth 3
}
catch {
    Write-Host "Error listing models: $($_.Exception.Message)"
}
