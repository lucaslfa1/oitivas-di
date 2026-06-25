using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var key = "6Ws98iJ3hijXQ5OtcZbIEJLKklcqZMJ2G1iIjTBSVY73aI3GRyElJQQJ99CBACHYHv6XJ3w3AAAAACOGPSvn"; // appsettings.json Key
        var region = "eastus2";
        var url = $"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15";

        var audioBytes = new byte[8000]; // Dummy silent buffer
        for (int i = 0; i < audioBytes.Length; i++) audioBytes[i] = 128; // PCM silent

        using var client = new HttpClient();
        using var content = new MultipartFormDataContent();
        
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
        content.Add(audioContent, "audio", "audio.wav");

        var definitionJson = "{\"locales\":[\"pt-BR\"],\"profanityFilterMode\":\"None\",\"diarization\":{\"enabled\":true,\"maxSpeakers\":2}}";
        content.Add(new StringContent(definitionJson, Encoding.UTF8, "application/json"), "definition");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        request.Content = content;

        Console.WriteLine("Enviando requisicao...");
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine(json);
    }
}
