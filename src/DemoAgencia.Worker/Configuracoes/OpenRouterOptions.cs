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
    public string VisionModel { get; set; } = "qwen/qwen2.5-vl-72b-instruct";
    public int MaxRetriesHttp { get; set; } = 5;
    public int BackoffBaseSegundos { get; set; } = 5;
    public int BackoffMaxSegundos { get; set; } = 60;
    public string[] FallbackModels { get; set; } = ["deepseek/deepseek-v3.2", "qwen/qwen3.7-plus", "openai/gpt-4o-mini"];
    public string[] ImageFallbackModels { get; set; } = [];
    public int Backoff429BaseSegundos { get; set; } = 2;
    public int Backoff429MaxSegundos { get; set; } = 30;
}
