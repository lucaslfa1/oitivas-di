namespace SinistroAPI.Interfaces;

public interface ITranscricaoService
{
    bool IsConfigured { get; }
    Task<string> TranscreverAudio(byte[] audioBytes, string mimeType, string? connectionId = null);
    Task<Dictionary<string, string>> ExtrairDadosOitiva(string transcricao);
}
