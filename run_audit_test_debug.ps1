$audioPath = "C:\Users\lucas.afonso\projetos\sentinel\part1.wav"
$url = "http://127.0.0.1:5252/api/transcrever"

Write-Host "Transcrevendo $audioPath..."
$response = curl.exe -s -v -X POST -H "Content-Type: multipart/form-data" -F "Arquivo=@$audioPath;type=audio/wav" $url
Write-Host "Raw Response: $response"
