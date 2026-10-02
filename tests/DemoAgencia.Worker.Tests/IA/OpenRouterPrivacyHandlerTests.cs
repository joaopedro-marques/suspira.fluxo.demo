using System.Net;
using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.IA;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class OpenRouterPrivacyHandlerTests
{
    private readonly Mock<ILogger<OpenRouterPrivacyHandler>> _loggerMock;
    private readonly IConfiguration _configuration;

    public OpenRouterPrivacyHandlerTests()
    {
        _loggerMock = new Mock<ILogger<OpenRouterPrivacyHandler>>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:DataCollection"] = "deny"
            })
            .Build();
    }

    [Fact]
    public async Task SendAsync_ShouldRenameMaxCompletionTokensToMaxTokens()
    {
        var testHandler = new CapturingTestHandler();
        testHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
        };

        var handler = new OpenRouterPrivacyHandler(_configuration, _loggerMock.Object)
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);

        var body = """{"model":"deepseek/deepseek-v3.2","max_completion_tokens":2000,"messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        var sentBody = await testHandler.CapturedContent!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.TryGetProperty("max_completion_tokens", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("max_tokens", out var maxTokens).Should().BeTrue();
        maxTokens.GetInt32().Should().Be(2000);
    }

    [Fact]
    public async Task SendAsync_ShouldInjectProviderWhenAbsent()
    {
        var testHandler = new CapturingTestHandler();
        testHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
        };

        var handler = new OpenRouterPrivacyHandler(_configuration, _loggerMock.Object)
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);

        var body = """{"model":"test","messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        var sentBody = await testHandler.CapturedContent!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.TryGetProperty("provider", out var provider).Should().BeTrue();
        provider.GetProperty("data_collection").GetString().Should().Be("deny");
    }

    [Fact]
    public async Task SendAsync_ShouldNotOverwriteExistingProvider()
    {
        var testHandler = new CapturingTestHandler();
        testHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
        };

        var handler = new OpenRouterPrivacyHandler(_configuration, _loggerMock.Object)
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);

        var body = """{"model":"test","provider":{"data_collection":"allow"},"messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        var sentBody = await testHandler.CapturedContent!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(sentBody);
        doc.RootElement.GetProperty("provider").GetProperty("data_collection").GetString().Should().Be("allow");
    }

    [Fact]
    public async Task SendAsync_OnError_ShouldLogResponseBody()
    {
        var errorBody = """{"error":{"message":"Invalid parameter","code":400}}""";
        var testHandler = new CapturingTestHandler();
        testHandler.Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
        };

        var handler = new OpenRouterPrivacyHandler(_configuration, _loggerMock.Object)
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);

        var body = """{"model":"test","messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var readableBody = await response.Content.ReadAsStringAsync();
        readableBody.Should().Be(errorBody);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Invalid parameter")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_OnError_ShouldTruncateLargeBody()
    {
        var largeBody = new string('x', 5000);
        var testHandler = new CapturingTestHandler();
        testHandler.Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(largeBody, Encoding.UTF8, "application/json")
        };

        var handler = new OpenRouterPrivacyHandler(_configuration, _loggerMock.Object)
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);

        var body = """{"model":"test","messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("...")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private class CapturingTestHandler : HttpMessageHandler
    {
        public HttpContent? CapturedContent { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedContent = request.Content;
            return Task.FromResult(Response);
        }
    }
}
