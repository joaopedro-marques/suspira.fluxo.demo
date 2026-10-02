using System.Text;
using System.Text.Json;

namespace DemoAgencia.Worker.IA;

public class OpenRouterPrivacyHandler : DelegatingHandler
{
    private readonly string _dataCollection;
    private readonly ILogger<OpenRouterPrivacyHandler> _logger;

    public OpenRouterPrivacyHandler(IConfiguration configuration, ILogger<OpenRouterPrivacyHandler> logger)
    {
        _dataCollection = configuration["OpenRouter:DataCollection"] ?? "deny";
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content != null && request.RequestUri != null)
        {
            var originalContent = await request.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(originalContent);
            var options = new JsonSerializerOptions { WriteIndented = false };

            var modifiedContent = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                modifiedContent[prop.Name] = prop.Value;
            }

            if (modifiedContent.Remove("max_completion_tokens", out var maxTokens))
            {
                modifiedContent.TryAdd("max_tokens", maxTokens);
            }

            if (!modifiedContent.ContainsKey("provider"))
            {
                var providerJson = JsonSerializer.SerializeToElement(new { data_collection = _dataCollection });
                modifiedContent["provider"] = providerJson;
            }

            var newJson = JsonSerializer.Serialize(modifiedContent, options);
            request.Content = new StringContent(newJson, Encoding.UTF8, "application/json");
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncated = body.Length > 2000 ? body[..2000] + "..." : body;
            _logger.LogError(
                "OpenRouter {Status} em {Uri}: {Body}",
                (int)response.StatusCode,
                request.RequestUri,
                truncated);
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return response;
    }
}
