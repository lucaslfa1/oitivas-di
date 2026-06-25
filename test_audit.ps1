$audioPath = "C:\Users\lucas.afonso\projetos\sentinel\part1.wav"
$url = "http://localhost:5252/api/transcrever"

Write-Host "Transcrevendo $audioPath..."
$response = Invoke-RestMethod -Uri $url -Method Post -Form @{
    Arquivo = Get-Item -Path $audioPath
} -ErrorAction Stop

Write-Host "Transcrição recebida:"
Write-Host $response.transcricao

$auditUrl = "http://localhost:5252/api/auditar"
Write-Host "`nAuditando transcrição..."
$auditBody = @{
    Transcricao = $response.transcricao
} | ConvertTo-Json

$auditResponse = Invoke-RestMethod -Uri $auditUrl -Method Post -Body $auditBody -ContentType "application/json" -ErrorAction Stop

Write-Host "Resultado da Auditoria:"
Write-Host $auditResponse.analise
