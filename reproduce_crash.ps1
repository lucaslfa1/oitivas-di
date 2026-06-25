$backendProc = Start-Process dotnet -ArgumentList "run --project C:\Users\lucas.afonso\projetos\sentinel\Backend\SinistroAPI.csproj --urls=http://127.0.0.1:5252" -PassThru -NoNewWindow
Start-Sleep -Seconds 10
Write-Host "Running Curl..."
try {
    $response = curl.exe -v -X POST -H "Content-Type: multipart/form-data" -F "Arquivo=@C:\Users\lucas.afonso\projetos\sentinel\part1.wav;type=audio/wav" http://127.0.0.1:5252/api/transcrever
    Write-Host "Response received"
} catch {
    Write-Host "Curl failed: $($_.Exception.Message)"
}
Start-Sleep -Seconds 2
Stop-Process -Id $backendProc.Id -Force -ErrorAction SilentlyContinue
