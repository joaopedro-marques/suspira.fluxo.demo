using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class OpenRouterOptions
{
    public const string Section = "OpenRouter";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string DataCollection { get; set; } = "deny";
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";
    public string ImageModel { get; set; } = "qwen/qwen-image-3-pro";
    public int MaxRetriesHttp { get; set; } = 5;
    public int BackoffBaseSegundos { get; set; } = 5;
    public int BackoffMaxSegundos { get; set; } = 60;
}
