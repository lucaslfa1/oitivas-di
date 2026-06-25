namespace SinistroAPI.Configuration;

/// <summary>
/// Configuracoes do Azure Speech Services (Speech-to-Text + Whisper)
/// </summary>
public class AzureSpeechSettings
{
    public bool Enabled { get; set; } = false;

    // Azure OpenAI Whisper
    public string SubscriptionKey { get; set; } = "";
    public string Region { get; set; } = "brazilsouth";
    public string Endpoint { get; set; } = "";
    public string DeploymentName { get; set; } = "whisper";

    // Azure Speech-to-Text (Fast Transcription API)
    public bool SpeechToTextEnabled { get; set; } = true;
    public string SpeechToTextKey { get; set; } = "";
    public string SpeechToTextRegion { get; set; } = "";
    public string SpeechToTextApiVersion { get; set; } = "2025-10-15";
    public string SpeechToTextLocale { get; set; } = "pt-BR";
    public bool SpeechToTextUseDiarization { get; set; } = true;
    public int SpeechToTextMaxSpeakers { get; set; } = 2;
    public bool SpeechToTextUsePhraseList { get; set; } = true;
    public List<string> SpeechToTextPhraseList { get; set; } = new();
}
