$audioPath = "C:\Users\lucas.afonso\projetos\sentinel\part1.wav"
$url = "http://127.0.0.1:5252/api/transcrever"

Write-Host "Transcrevendo $audioPath..."
$response = curl.exe -s -X POST -H "Content-Type: multipart/form-data" -F "Arquivo=@$audioPath;type=audio/wav" $url
$json = $response | ConvertFrom-Json

if ($json.transcricao) {
    Write-Host "`nTranscrição recebida com sucesso!" -ForegroundColor Green
    Write-Host "--------------------------------"
    Write-Host $json.transcricao
    Write-Host "--------------------------------"

    $auditUrl = "http://127.0.0.1:5252/api/auditar"
    Write-Host "`nAuditando transcrição..."
    $auditBody = @{
        Transcricao = $json.transcricao
        Roteiro = "Auditoria de Conformidade: Verifique se o operador se identificou, se explicou o motivo do contato e se perguntou os fatos do acidente de forma clara."
    } | ConvertTo-Json

    $auditResponse = curl.exe -s -X POST -H "Content-Type: application/json" -d $auditBody $auditUrl
    $auditJson = $auditResponse | ConvertFrom-Json
    
    Write-Host "`nResultado da Auditoria:" -ForegroundColor Cyan
    Write-Host "--------------------------------"
    Write-Host $auditJson.analise
    Write-Host "--------------------------------"
} else {
    Write-Host "Falha na transcrição: $($json.error)" -ForegroundColor Red
}
