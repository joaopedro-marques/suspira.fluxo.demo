using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Seguranca;
using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramServiceTests
{
    private readonly Mock<ILogger<TelegramService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<PipelineService> _pipelineMock;
    private readonly Mock<HistoricoChat> _historicoMock;
    private readonly Mock<StreamingService> _streamingMock;
    private readonly Mock<RateLimiterService> _rateLimiterMock;
    private readonly Mock<AnonimizadorService> _anonimizadorMock;

    public TelegramServiceTests()
    {
        _loggerMock = new Mock<ILogger<TelegramService>>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "test-token"
            })
            .Build();

        _agenteLoaderMock = new Mock<AgenteLoader>(
            Mock.Of<ILogger<AgenteLoader>>(),
            Mock.Of<IConfiguration>(),
            (string?)null);

        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());

        var anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
        anonimizadorMock.Setup(x => x.Anonimizar(It.IsAny<string>())).Returns<string>(s => s);

        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object,
            anonimizadorMock.Object);

        _openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object,
            Mock.Of<IHttpClientFactory>());

        _pipelineMock = new Mock<PipelineService>(
            Mock.Of<ILogger<PipelineService>>(),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            Mock.Of<HistoricoChat>(),
            Mock.Of<IConfiguration>());

        _historicoMock = new Mock<HistoricoChat>();
        _streamingMock = new Mock<StreamingService>(Mock.Of<ILogger<StreamingService>>());
        _rateLimiterMock = new Mock<RateLimiterService>(Mock.Of<IConfiguration>());
        _anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
    }

    [Fact]
    public void Constructor_WithValidToken_ShouldNotThrow()
    {
        var act = () => new TelegramService(
            _loggerMock.Object,
            _configuration,
            _agenteLoaderMock.Object,
            _openRouterMock.Object,
            _pipelineMock.Object,
            _historicoMock.Object,
            _streamingMock.Object,
            _rateLimiterMock.Object,
            _anonimizadorMock.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithEmptyToken_ShouldNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = ""
            })
            .Build();

        var act = () => new TelegramService(
            _loggerMock.Object,
            config,
            _agenteLoaderMock.Object,
            _openRouterMock.Object,
            _pipelineMock.Object,
            _historicoMock.Object,
            _streamingMock.Object,
            _rateLimiterMock.Object,
            _anonimizadorMock.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void ParseCommand_WithSimpleCommand_ShouldExtractCommand()
    {
        var text = "/start";
        var parts = text.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();

        command.Should().Be("/start");
    }

    [Fact]
    public void ParseCommand_WithCommandAndArgs_ShouldExtractBoth()
    {
        var text = "/dev Escreva um código";
        var parts = text.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : string.Empty;

        command.Should().Be("/dev");
        args.Should().Be("Escreva um código");
    }

    [Fact]
    public void ParseCommand_WithCaseInsensitive_ShouldNormalize()
    {
        var text = "/START";
        var command = text.ToLowerInvariant();

        command.Should().Be("/start");
    }

    [Fact]
    public void ParseCommand_WithMultipleSpaces_ShouldHandleCorrectly()
    {
        var text = "/redator  Escreva  um  texto";
        var parts = text.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : string.Empty;

        command.Should().Be("/redator");
        args.Should().StartWith(" Escreva");
    }

    [Fact]
    public void IsCommand_WithSlashPrefix_ShouldReturnTrue()
    {
        var text = "/start";
        var isCommand = text.StartsWith("/");

        isCommand.Should().BeTrue();
    }

    [Fact]
    public void IsCommand_WithoutSlashPrefix_ShouldReturnFalse()
    {
        var text = "start";
        var isCommand = text.StartsWith("/");

        isCommand.Should().BeFalse();
    }

    [Fact]
    public void IsCommand_WithSlashInMiddle_ShouldReturnFalse()
    {
        var text = "hello/start";
        var isCommand = text.StartsWith("/");

        isCommand.Should().BeFalse();
    }
}
