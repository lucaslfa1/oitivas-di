namespace SinistroAPI.Configuration;

/// <summary>
/// Upload size limits (in MB) for request and per media type.
/// </summary>
public class UploadLimitsOptions
{
    public int MaxRequestBodyMB { get; set; } = 500;
    public int MaxImageUploadMB { get; set; } = 25;
    public int MaxAudioUploadMB { get; set; } = 200;
    public int MaxVideoUploadMB { get; set; } = 500;
    public int MaxVertexInlineMB { get; set; } = 20;

    public long MaxRequestBodyBytes => (long)MaxRequestBodyMB * 1024 * 1024;
    public long MaxImageUploadBytes => (long)MaxImageUploadMB * 1024 * 1024;
    public long MaxAudioUploadBytes => (long)MaxAudioUploadMB * 1024 * 1024;
    public long MaxVideoUploadBytes => (long)MaxVideoUploadMB * 1024 * 1024;
    public long MaxVertexInlineBytes => (long)MaxVertexInlineMB * 1024 * 1024;
}
