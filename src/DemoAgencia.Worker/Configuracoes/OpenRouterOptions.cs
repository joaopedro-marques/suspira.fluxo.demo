namespace DemoAgencia.Worker.Configuracoes;

public class OpenRouterOptions
{
    public const string Section = "OpenRouter";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string DataCollection { get; set; } = "deny";
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";
}
