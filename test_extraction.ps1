
$filePath = "TestFile.mp3" # Substitua por um arquivo real se tiver
$url = "http://localhost:8000/extract/audio"

if (-not (Test-Path $filePath)) {
    Write-Host "Criando arquivo dummy..."
    Set-Content -Path "dummy.txt" -Value "dummy content"
    $filePath = "dummy.txt"
}

Write-Host "Testando endpoint de extração..."
try {
    $response = Invoke-RestMethod -Uri $url -Method Post -InFile $filePath -ContentType "multipart/form-data"
    Write-Host "Sucesso! Resposta:"
    $response | ConvertTo-Json
} catch {
    Write-Host "Erro:"
    $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}
