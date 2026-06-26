using SinistroAPI.Services;

namespace SinistroAPI.Interfaces;

public interface IMediaProcessorService
{
    bool IsEnabled { get; }
    Task<SentimentResult?> AnalyzeSentimentAsync(byte[] audioBytes, string mimeType);
    Task<ProcessedAudioResult?> MergeAudiosAsync(List<byte[]> audioFiles, string mimeType);
    Task<string?> AnnotateImageAsync(string imageBase64, List<ImageAnnotation> annotations);
}
