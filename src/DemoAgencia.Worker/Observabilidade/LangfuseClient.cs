using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.Contracts;

namespace DemoAgencia.Worker.Observabilidade;

public class LangfuseClient
{
    private readonly ILogger<LangfuseClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public LangfuseClient(ILogger<LangfuseClient> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var host = _configuration["Langfuse:Host"] ?? "https://cloud.langfuse.com";
        var publicKey = _configuration["Langfuse:PublicKey"] ?? "";
        var secretKey = _configuration["Langfuse:SecretKey"] ?? "";

        _httpClient = new HttpClient { BaseAddress = new Uri(host) };
        
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public virtual async Task EnviarTraceAsync(LangfuseTrace trace, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_configuration["Langfuse:PublicKey"]))
        {
            _logger.LogDebug("Langfuse nao configurado, ignorando trace");
            return;
        }

        try
        {
            var payload = new
            {
                batch = new object[]
                {
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        type = "trace-create",
                        timestamp = DateTime.UtcNow.ToString("o"),
                        body = new
                        {
                            id = trace.Id,
                            name = trace.Name,
                            userId = trace.UserId,
                            metadata = trace.Metadata,
                            tags = trace.Tags
                        }
                    },
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        type = "generation-create",
                        timestamp = DateTime.UtcNow.ToString("o"),
                        body = new
                        {
                            id = trace.ObservationId,
                            traceId = trace.Id,
                            name = trace.ObservationName,
                            type = "GENERATION",
                            model = trace.Model,
                            input = trace.Input,
                            output = trace.Output,
                            usage = new
                            {
                                promptTokens = trace.PromptTokens,
                                completionTokens = trace.CompletionTokens,
                                totalTokens = trace.PromptTokens + trace.CompletionTokens
                            },
                            metadata = trace.ObservationMetadata,
                            startTime = trace.StartTime.ToString("o"),
                            endTime = trace.EndTime.ToString("o")
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/public/ingestion", content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Trace enviado ao Langfuse: {TraceId}", trace.Id);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Erro ao enviar trace ao Langfuse: {StatusCode} - {Error}", response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excecao ao enviar trace ao Langfuse");
        }
    }
}
