using System.Net;
using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.IA;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class OpenRouterReasoningHandlerTests
{
    private readonly string _preflightBody = """{"model":"deepseek/deepseek-v3.2","max_completion_tokens":4000,"temperature":0.2,"messages":[{"role":"system","content":"persona"},{"role":"user","content":"instrucoes"}],"provider":{"data_collection":"deny"}}""";

    [Fact]
    public async Task SendAsync_OnChatCompletions_ShouldInjectReasoningEnabledFalse()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: true);

        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.TryGetProperty("reasoning", out var reasoningEl).Should().BeTrue();
        reasoningEl.TryGetProperty("enabled", out var enabledEl).Should().BeTrue();
        enabledEl.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_OnChatCompletions_ShouldPreserveMessagesAsArray()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: true);

        using var doc = JsonDocument.Parse(sentBody);
        var messagesEl = doc.RootElement.GetProperty("messages");
        messagesEl.ValueKind.Should().Be(JsonValueKind.Array, "messages must remain a JSON array, not a double-encoded string");
        messagesEl.GetArrayLength().Should().Be(2);
        messagesEl[0].GetProperty("role").GetString().Should().Be("system");
        messagesEl[0].GetProperty("content").GetString().Should().Be("persona");
        messagesEl[1].GetProperty("role").GetString().Should().Be("user");
        messagesEl[1].GetProperty("content").GetString().Should().Be("instrucoes");
    }

    [Fact]
    public async Task SendAsync_OnChatCompletions_ShouldPreserveProviderAsObject()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: true);

        using var doc = JsonDocument.Parse(sentBody);
        var providerEl = doc.RootElement.GetProperty("provider");
        providerEl.ValueKind.Should().Be(JsonValueKind.Object, "provider must remain a JSON object, not a double-encoded string");
        providerEl.GetProperty("data_collection").GetString().Should().Be("deny");
    }

    [Fact]
    public async Task SendAsync_OnChatCompletions_ShouldPreserveNumericFieldsExactly()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: true);

        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.GetProperty("max_completion_tokens").GetInt32().Should().Be(4000);
        doc.RootElement.GetProperty("temperature").GetDouble().Should().BeApproximately(0.2, 1e-9);
        doc.RootElement.GetProperty("model").GetString().Should().Be("deepseek/deepseek-v3.2");
    }

    [Fact]
    public async Task SendAsync_WhenNotActive_ShouldPassThroughUnchanged()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: false);

        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
        doc.RootElement.GetProperty("model").GetString().Should().Be("deepseek/deepseek-v3.2");
    }

    [Fact]
    public async Task SendAsync_OnNonChatCompletionsPath_ShouldNotRewrite()
    {
        var sentBody = await SendThroughHandler(_preflightBody, activate: true, requestPath: "https://openrouter.ai/api/v1/images");

        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    private static async Task<string> SendThroughHandler(string body, bool activate, string requestPath = "https://openrouter.ai/api/v1/chat/completions")
    {
        var capture = new CapturingTestHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }
        };
        var handler = new ReasoningDisablingHandler { InnerHandler = capture };
        var client = new HttpClient(handler);

        ReasoningDisablingHandler.IsActive = activate;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestPath)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            await client.SendAsync(request);
            return capture.CapturedBody ?? throw new InvalidOperationException("handler did not forward a body");
        }
        finally
        {
            ReasoningDisablingHandler.IsActive = false;
        }
    }

    private class CapturingTestHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
