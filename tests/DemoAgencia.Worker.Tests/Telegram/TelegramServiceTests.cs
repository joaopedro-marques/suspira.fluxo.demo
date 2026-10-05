using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramServiceTests
{
    private readonly Mock<ILogger<TelegramService>> _loggerMock;
    private readonly TelegramOptions _telegramOptions;
    private readonly Mock<IAnalisadorImagem> _analisadorImagemMock;
    private readonly Mock<RateLimiterService> _rateLimiterMock;
    private readonly Mock<ITelegramGatewayFactory> _gatewayFactoryMock;
    private readonly Mock<RouterService> _routerMock;
    private readonly ConversaPendenteStore _pendencias;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IReferenciasCliente> _referenciasMock;

    public TelegramServiceTests()
    {
        _loggerMock = new Mock<ILogger<TelegramService>>();
        _telegramOptions = new TelegramOptions { BotToken = "test-token" };

        _analisadorImagemMock = new Mock<IAnalisadorImagem>();

        _referenciasMock = new Mock<IReferenciasCliente>();
        _rateLimiterMock = new Mock<RateLimiterService>(TestOptions.Create(new SegurancaOptions()));
        _gatewayFactoryMock = new Mock<ITelegramGatewayFactory>();

        _routerMock = new Mock<RouterService>(
            _referenciasMock.Object,
            Mock.Of<IServicoChat>(),
            new ConversaPendenteStore(
                TestOptions.Create(new PreFlightOptions()),
                TimeProvider.System,
                Mock.Of<ILogger<ConversaPendenteStore>>()),
            TestOptions.Create(new PreFlightOptions()),
            Mock.Of<ILogger<RouterService>>());

        _pendencias = new ConversaPendenteStore(
            TestOptions.Create(new PreFlightOptions()),
            TimeProvider.System,
            Mock.Of<ILogger<ConversaPendenteStore>>());

        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    [Fact]
    public void Constructor_WithValidToken_ShouldNotThrow()
    {
        var act = () => new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithEmptyToken_ShouldNotThrow()
    {
        var options = new TelegramOptions { BotToken = "" };

        var act = () => new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

        act.Should().NotThrow();
    }
}
