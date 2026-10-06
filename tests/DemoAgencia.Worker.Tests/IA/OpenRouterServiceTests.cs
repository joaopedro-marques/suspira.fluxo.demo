using System.Net;
using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Observabilidade;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class OpenRouterServiceTests
{
    [Fact]
    public void OpenRouterOptions_VisionModel_ShouldHaveDefault()
    {
        var options = new OpenRouterOptions();
        options.VisionModel.Should().Be("qwen/qwen2.5-vl-72b-instruct");
    }

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
        var result = JsonHelper.ExtrairJson(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtrairJson_WithNestedJson_ShouldExtractOuter()
    {
        var input = "{\"outer\": {\"inner\": \"value\"}}";
        var result = JsonHelper.ExtrairJson(input);
        result.Should().Be(input);
    }

    [Fact]
    public void ExtrairJson_WithMarkdownCodeBlock_ShouldExtractJson()
    {
        var input = "```json\n{\"acao\": \"pipeline\"}\n```";
        var result = JsonHelper.ExtrairJson(input);
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

        result.Sucesso.Should().BeTrue();
        result.Bytes.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        result.Erro.Should().BeNull();
    }

    [Fact]
    public async Task GerarImagemAsync_ShouldUseImageModelFromConfig()
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
        options.ImageModel = "custom/image-model";
        var service = CreateService(httpClientFactory, options);

        await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        var body = await handler.CapturedContent!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("model").GetString().Should().Be("custom/image-model");
    }

    [Fact]
    public async Task GerarImagemAsync_OnHttpError_ShouldReturnErrorReason()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory, CreateOptions());

        var result = await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        result.Sucesso.Should().BeFalse();
        result.Bytes.Should().BeNull();
        result.Erro.Should().Contain("404");
    }

    [Fact]
    public async Task GerarImagemAsync_OnHttpError_ShouldFinalizeTrace()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var httpClientFactory = CreateHttpClientFactory(handler);
        var langfuseMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            CreateLangfuseClient(),
            CreateAnonimizador());
        langfuseMock.Setup(x => x.FinalizarTraceAsync(
            It.IsAny<DemoAgencia.Worker.Contracts.LangfuseTraceContext>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OpenRouterService>>();
        var optionsMock = new Mock<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>();
        optionsMock.Setup(o => o.Value).Returns(CreateOptions());
        var service = new OpenRouterService(loggerMock.Object, optionsMock.Object, langfuseMock.Object, httpClientFactory);

        await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        langfuseMock.Verify(x => x.FinalizarTraceAsync(
            It.IsAny<DemoAgencia.Worker.Contracts.LangfuseTraceContext>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChamarAgenteAsync_WithRouterEtapa_ShouldSendReasoningDisabled()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                ChatCompletionResponse("resposta ok"),
                Encoding.UTF8, "application/json")
        };

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory, CreateOptions());

        await service.ChamarAgenteAsync(1, "persona", "test-model", "instrucoes", "router");

        handler.CapturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        root.TryGetProperty("reasoning", out var reasoningEl).Should().BeTrue();
        reasoningEl.TryGetProperty("enabled", out var enabledEl).Should().BeTrue();
        enabledEl.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ChamarAgenteAsync_WithNonRouterEtapa_ShouldNotSendReasoning()
    {
        var handler = new CapturingTestHandler();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                ChatCompletionResponse("resposta ok"),
                Encoding.UTF8, "application/json")
        };

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory, CreateOptions());

        await service.ChamarAgenteAsync(1, "persona", "test-model", "instrucoes", "chat-user");

        handler.CapturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        root.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ChamarAgenteAsync_On429_ShouldFallbackToNextModel()
    {
        var handler = new CapturingTestHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                ChatCompletionResponse("fallback ok"),
                Encoding.UTF8, "application/json")
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        options.FallbackModels = ["fallback/model-a", "fallback/model-b"];
        options.Backoff429BaseSegundos = 0;
        var service = CreateService(httpClientFactory, options);

        var result = await service.ChamarAgenteAsync(1, "persona", "primary/model", "instrucoes", "email_copy");

        result.Should().Be("fallback ok");
        handler.CapturedBodies.Should().HaveCount(2);
        using var doc = JsonDocument.Parse(handler.CapturedBodies[1]!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("fallback/model-a");
    }

    [Fact]
    public async Task ChamarAgenteAsync_On429AllModels_ShouldThrow()
    {
        var handler = new CapturingTestHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        options.FallbackModels = ["fallback/model-a"];
        options.Backoff429BaseSegundos = 0;
        var service = CreateService(httpClientFactory, options);

        var act = () => service.ChamarAgenteAsync(1, "persona", "primary/model", "instrucoes", "email_copy");

        await act.Should().ThrowAsync<HttpOperationException>();
        handler.CapturedBodies.Should().HaveCount(2);
    }

    [Fact]
    public async Task ChamarAgenteAsync_OnNon429Error_ShouldNotFallback()
    {
        var handler = new CapturingTestHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        options.FallbackModels = ["fallback/model-a"];
        var service = CreateService(httpClientFactory, options);

        var act = () => service.ChamarAgenteAsync(1, "persona", "primary/model", "instrucoes", "email_copy");

        await act.Should().ThrowAsync<HttpOperationException>();
        handler.CapturedBodies.Should().HaveCount(1);
    }

    [Fact]
    public async Task GerarImagemAsync_On429_ShouldFallbackToNextImageModel()
    {
        var handler = new CapturingTestHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"b64_json\":\"AQID\"}]}",
                Encoding.UTF8, "application/json")
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        options.ImageModel = "primary/image-model";
        options.ImageFallbackModels = ["fallback/image-model"];
        options.Backoff429BaseSegundos = 0;
        var service = CreateService(httpClientFactory, options);

        var result = await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        result.Sucesso.Should().BeTrue();
        result.Bytes.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        handler.CapturedBodies.Should().HaveCount(2);
        using var doc = JsonDocument.Parse(handler.CapturedBodies[1]!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("fallback/image-model");
    }

    [Fact]
    public async Task GerarImagemAsync_On429AllModels_ShouldReturnError()
    {
        var handler = new CapturingTestHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var httpClientFactory = CreateHttpClientFactory(handler);
        var options = CreateOptions();
        options.ImageModel = "primary/image-model";
        options.ImageFallbackModels = ["fallback/image-model"];
        options.Backoff429BaseSegundos = 0;
        var service = CreateService(httpClientFactory, options);

        var result = await service.GerarImagemAsync(1, "um gato", CancellationToken.None);

        result.Sucesso.Should().BeFalse();
        result.Erro.Should().Contain("429");
        handler.CapturedBodies.Should().HaveCount(2);
    }

    private static string ChatCompletionResponse(string content)
    {
        return $"{{\"id\":\"chatcmpl-123\",\"object\":\"chat.completion\",\"created\":1234567890,\"model\":\"test-model\",\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":\"{content}\"}},\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":10,\"completion_tokens\":10,\"total_tokens\":20}}}}";
    }

    private static IHttpClientFactory CreateHttpClientFactory(CapturingTestHandler handler)
    {
        var reasoningHandler = new ReasoningDisablingHandler { InnerHandler = handler };
        var client = new HttpClient(reasoningHandler);
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

        var langfuseMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            CreateLangfuseClient(),
            CreateAnonimizador());

        return new OpenRouterService(loggerMock.Object, optionsMock.Object, langfuseMock.Object, httpClientFactory);
    }

    private static LangfuseClient CreateLangfuseClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Langfuse:Host"] = "https://cloud.langfuse.com",
                ["Langfuse:PublicKey"] = "",
                ["Langfuse:SecretKey"] = ""
            })
            .Build();

        return new LangfuseClient(
            Mock.Of<ILogger<LangfuseClient>>(),
            configuration);
    }

    private static DemoAgencia.Worker.Seguranca.AnonimizadorService CreateAnonimizador()
    {
        var mock = new Mock<DemoAgencia.Worker.Seguranca.AnonimizadorService>(
            Mock.Of<Microsoft.Extensions.Options.IOptions<DemoAgencia.Worker.Configuracoes.SegurancaOptions>>());
        return mock.Object;
    }

    private class CapturingTestHandler : HttpMessageHandler
    {
        public Uri? CapturedUri { get; private set; }
        public HttpContent? CapturedContent { get; private set; }
        public string? CapturedBody { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<string?> CapturedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedContent = request.Content;
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                CapturedBody = body;
                CapturedBodies.Add(body);
            }
            return Responses.Count > 0 ? Responses.Dequeue() : Response;
        }
    }
}
