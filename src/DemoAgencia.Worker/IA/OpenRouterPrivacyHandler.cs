using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DemoAgencia.Worker.IA;

public class OpenRouterPrivacyHandler : DelegatingHandler
{
    private readonly string _dataCollection;

    public OpenRouterPrivacyHandler(IConfiguration configuration)
    {
        _dataCollection = configuration["OpenRouter:DataCollection"] ?? "deny";
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

            if (!modifiedContent.ContainsKey("provider"))
            {
                var providerJson = JsonSerializer.SerializeToElement(new { data_collection = _dataCollection });
                modifiedContent["provider"] = providerJson;
            }

            var newJson = JsonSerializer.Serialize(modifiedContent, options);
            request.Content = new StringContent(newJson, Encoding.UTF8, "application/json");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
