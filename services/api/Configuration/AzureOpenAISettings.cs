namespace SinistroAPI.Configuration;

public class AzureOpenAISettings
{
    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DeploymentName { get; set; } = "gpt-4o";
    public string Region { get; set; } = "brazilsouth";
}
