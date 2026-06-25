using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço para upload de arquivos grandes via Gemini File API
/// Suporta arquivos de até 2GB
/// </summary>
public class GeminiFileApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiFileApiService> _logger;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public GeminiFileApiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiFileApiService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(30); // Timeout maior para uploads grandes
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Faz upload de um arquivo para a File API do Gemini
    /// Retorna o URI do arquivo para usar nas requisições
    /// </summary>
    public async Task<string> UploadFileAsync(byte[] fileBytes, string mimeType, string displayName)
    {
        if (!IsConfigured) throw new InvalidOperationException("Gemini não configurado.");

        _logger.LogInformation("Iniciando upload de arquivo ({Size} MB) via File API", fileBytes.Length / 1024.0 / 1024.0);

        // Passo 1: Iniciar upload resumível
        var initUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={_apiKey}";
        
        var initRequest = new HttpRequestMessage(HttpMethod.Post, initUrl);
        initRequest.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        initRequest.Headers.Add("X-Goog-Upload-Command", "start");
        initRequest.Headers.Add("X-Goog-Upload-Header-Content-Length", fileBytes.Length.ToString());
        initRequest.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
        
        var metadata = new { file = new { display_name = displayName } };
        initRequest.Content = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");

        var initResponse = await _httpClient.SendAsync(initRequest);
        
        if (!initResponse.IsSuccessStatusCode)
        {
            var error = await initResponse.Content.ReadAsStringAsync();
            _logger.LogError("Erro ao iniciar upload: {Error}", error);
            throw new Exception($"Erro ao iniciar upload: {error}");
        }

        // Pegar URL de upload da resposta
        var uploadUrl = initResponse.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault();
        if (string.IsNullOrEmpty(uploadUrl))
        {
            throw new Exception("Não foi possível obter URL de upload");
        }

        _logger.LogInformation("URL de upload obtida, enviando dados...");

        // Passo 2: Enviar o arquivo
        var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        uploadRequest.Headers.Add("X-Goog-Upload-Offset", "0");
        uploadRequest.Content = new ByteArrayContent(fileBytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = fileBytes.Length;

        var uploadResponse = await _httpClient.SendAsync(uploadRequest);
        
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var error = await uploadResponse.Content.ReadAsStringAsync();
            _logger.LogError("Erro ao fazer upload: {Error}", error);
            throw new Exception($"Erro ao fazer upload: {error}");
        }

        var responseContent = await uploadResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        
        var fileUri = doc.RootElement.GetProperty("file").GetProperty("uri").GetString();
        var fileName = doc.RootElement.GetProperty("file").GetProperty("name").GetString();
        
        _logger.LogInformation("Upload concluído! File URI: {FileUri}", fileUri);

        // Passo 3: Aguardar processamento (estado ACTIVE)
        await WaitForFileProcessing(fileName!);

        return fileUri!;
    }

    /// <summary>
    /// Aguarda o arquivo ser processado pelo Gemini
    /// </summary>
    private async Task WaitForFileProcessing(string fileName)
    {
        var maxAttempts = 30;
        var delayMs = 2000;

        for (int i = 0; i < maxAttempts; i++)
        {
            var statusUrl = $"https://generativelanguage.googleapis.com/v1beta/{fileName}?key={_apiKey}";
            var response = await _httpClient.GetAsync(statusUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                
                var state = doc.RootElement.GetProperty("state").GetString();
                _logger.LogInformation("Estado do arquivo: {State}", state);

                if (state == "ACTIVE")
                {
                    return; // Pronto para uso!
                }
                else if (state == "FAILED")
                {
                    throw new Exception("Processamento do arquivo falhou no Gemini");
                }
            }

            await Task.Delay(delayMs);
        }

        throw new Exception("Timeout aguardando processamento do arquivo");
    }

    /// <summary>
    /// Deleta um arquivo da File API
    /// </summary>
    public async Task DeleteFileAsync(string fileName)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/{fileName}?key={_apiKey}";
        await _httpClient.DeleteAsync(url);
    }
}
