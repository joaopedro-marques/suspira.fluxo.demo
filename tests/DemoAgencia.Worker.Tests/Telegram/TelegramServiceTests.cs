using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
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
    private readonly Mock<IAgentesCatalogo> _agenteLoaderMock;
    private readonly Mock<IStreamingChat> _openRouterMock;
    private readonly Mock<IAnalisadorImagem> _analisadorImagemMock;
    private readonly Mock<OrquestradorLoopService> _loopMock;
    private readonly Mock<IHistoricoChat> _historicoMock;
    private readonly Mock<IStreamingService> _streamingMock;
    private readonly Mock<RateLimiterService> _rateLimiterMock;
    private readonly Mock<ITelegramGatewayFactory> _gatewayFactoryMock;
    private readonly Mock<RouterService> _routerMock;
    private readonly Mock<IReferenciasCliente> _referenciasMock;
    private readonly ConversaPendenteStore _pendencias;

    public TelegramServiceTests()
    {
        _loggerMock = new Mock<ILogger<TelegramService>>();
        _telegramOptions = new TelegramOptions { BotToken = "test-token" };

        _agenteLoaderMock = new Mock<IAgentesCatalogo>();

        _openRouterMock = new Mock<IStreamingChat>();
        _analisadorImagemMock = new Mock<IAnalisadorImagem>();

        _referenciasMock = new Mock<IReferenciasCliente>();
        var enriquecedor = new EnriquecedorContextoCliente(
            _referenciasMock.Object,
            _analisadorImagemMock.Object,
            Mock.Of<ILogger<EnriquecedorContextoCliente>>());

        _loopMock = new Mock<OrquestradorLoopService>(
            Mock.Of<ILogger<OrquestradorLoopService>>(),
            TestOptions.Create(new LoopOptions()),
            Mock.Of<IServicoChat>(),
            _agenteLoaderMock.Object,
            _referenciasMock.Object,
            new FerramentaRegistry(),
            enriquecedor);

        _historicoMock = new Mock<IHistoricoChat>();
        _streamingMock = new Mock<IStreamingService>();
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
    }

    [Fact]
    public void Constructor_WithValidToken_ShouldNotThrow()
    {
        var act = () => new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _agenteLoaderMock.Object,
            _openRouterMock.Object,
            _analisadorImagemMock.Object,
            _loopMock.Object,
            _historicoMock.Object,
            _streamingMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithEmptyToken_ShouldNotThrow()
    {
        var options = new TelegramOptions { BotToken = "" };

        var act = () => new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _agenteLoaderMock.Object,
            _openRouterMock.Object,
            _analisadorImagemMock.Object,
            _loopMock.Object,
            _historicoMock.Object,
            _streamingMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias);

        act.Should().NotThrow();
    }
}
