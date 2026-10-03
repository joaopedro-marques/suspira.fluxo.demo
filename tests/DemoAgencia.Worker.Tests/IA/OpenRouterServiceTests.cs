using System.Net;
using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Observabilidade;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class OpenRouterServiceTests
{
    [Theory]
    [InlineData("{\"acao\": \"pipeline\"}", "{\"acao\": \"pipeline\"}")]
    [InlineData("Aqui esta o JSON: {\"acao\": \"direta\"} ok", "{\"acao\": \"direta\"}")]
    [InlineData("{\"a\": 1, \"b\": {\"c\": 2}}", "{\"a\": 1, \"b\": {\"c\": 2}}")]
    [InlineData("Texto sem JSON", null)]
    [InlineData("", null)]
    [InlineData("{}", "{}")]
    [InlineData("{invalido}", "{invalido}")]
    public void ExtrairJson_ShouldExtractJsonFromText(string input, string? expected)
    {
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtrairJson_WithNestedJson_ShouldExtractOuter()
    {
        var input = "{\"outer\": {\"inner\": \"value\"}}";
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be(input);
    }

    [Fact]
    public void ExtrairJson_WithMarkdownCodeBlock_ShouldExtractJson()
    {
        var input = "```json\n{\"acao\": \"pipeline\"}\n```";
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be("{\"acao\": \"pipeline\"}");
    }

    [Fact]
    public async Task GerarImagemAsync_ShouldPostToImagesEndpoint_NotImagesGenerations()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"b64_json\":\"AQID\"}]}",
                Encoding.UTF8, "application/json")
        };

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions(baseUrl: "https://openrouter.ai/api/v1");
        var service = CreateService(httpClientFactory, options);

        await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        handler.CapturedUri.Should().NotBeNull();
        handler.CapturedUri!.AbsoluteUri.Should().EndWith("/images");
        handler.CapturedUri.AbsoluteUri.Should().NotContain("/images/generations");
    }

    [Fact]
    public async Task GerarImagemAsync_ShouldUseOpenRouterImageSchema_NoResponseFormatNoProvider()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"b64_json\":\"AQID\"}]}",
                Encoding.UTF8, "application/json")
        };

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        var service = CreateService(httpClientFactory, options);

        await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        var body = await handler.CapturedContent!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("model", out _).Should().BeTrue();
        root.TryGetProperty("prompt", out var promptEl).Should().BeTrue();
        promptEl.GetString().Should().Be("um gato");
        root.TryGetProperty("n", out var nEl).Should().BeTrue();
        nEl.GetInt32().Should().Be(1);
        root.TryGetProperty("response_format", out _).Should().BeFalse();
        root.TryGetProperty("provider", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GerarImagemAsync_OnSuccess_ShouldReturnDecodedBytes()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"b64_json\":\"AQID\"}]}",
                Encoding.UTF8, "application/json")
        };

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory, CreateOptions());

        var result = await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task GerarImagemAsync_OnHttpError_ShouldReturnNull()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory, CreateOptions());

        var result = await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        result.Should().BeNull();
    }

    private static IHttpClientFactory CreateHttpClientFactory(CapturingTestHandler handler)
    {
        var client = new HttpClient(handler);
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient("OpenRouter")).Returns(client);
        return mock.Object;
    }

    private static OpenRouterOptions CreateOptions(string baseUrl = "https://openrouter.ai/api/v1")
    {
        return new OpenRouterOptions
        {
            ApiKey = "test-key",
            BaseUrl = baseUrl,
            DataCollection = "deny",
            DefaultModel = "openai/gpt-4o-mini"
        };
    }

    private static OpenRouterService CreateService(
        IHttpClientFactory httpClientFactory,
        OpenRouterOptions options)
    {
        var loggerMock = new Mock<ILogger<OpenRouterService>>();
        var optionsMock = new Mock<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Langfuse:Host"] = "https://cloud.langfuse.com",
                ["Langfuse:PublicKey"] = "",
                ["Langfuse:SecretKey"] = ""
            })
            .Build();

        var langfuseClient = new LangfuseClient(
            Mock.Of<ILogger<LangfuseClient>>(),
            configuration);

        var anonMock = new Mock<DemoAgencia.Worker.Seguranca.AnonimizadorService>(
            Mock.Of<Microsoft.Extensions.Options.IOptions<DemoAgencia.Worker.Configuracoes.SegurancaOptions>>());

        var langfuseMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClient,
            anonMock.Object);

        return new OpenRouterService(loggerMock.Object, optionsMock.Object, langfuseMock.Object, httpClientFactory);
    }

    private class CapturingTestHandler : HttpMessageHandler
    {
        public Uri? CapturedUri { get; private set; }
        public HttpContent? CapturedContent { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedContent = request.Content;
            return Task.FromResult(Response);
        }
    }
}
