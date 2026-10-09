using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Onboarding;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramServiceOnboardingTests
{
    private readonly Mock<ILogger<TelegramService>> _loggerMock;
    private readonly TelegramOptions _telegramOptions;
    private readonly Mock<IAnalisadorImagem> _analisadorImagemMock;
    private readonly Mock<RateLimiterService> _rateLimiterMock;
    private readonly Mock<ITelegramGatewayFactory> _gatewayFactoryMock;
    private readonly Mock<RouterService> _routerMock;
    private readonly ConversaPendenteStore _pendencias;
    private readonly OnboardingStore _onboardingStore;
    private readonly OnboardingService _onboardingService;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IReferenciasCliente> _referenciasMock;

    public TelegramServiceOnboardingTests()
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
            Mock.Of<IAgentesCatalogo>(),
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
        _onboardingStore = new OnboardingStore(
            TestOptions.Create(new PreFlightOptions()),
            TimeProvider.System,
            Mock.Of<ILogger<OnboardingStore>>());
        _onboardingService = new OnboardingService(
            _onboardingStore,
            _referenciasMock.Object,
            Mock.Of<ILogger<OnboardingService>>());
        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    private TelegramService CriarService() => new(
        _loggerMock.Object,
        TestOptions.Create(_telegramOptions),
        TestOptions.Create(new ConcorrenciaOptions()),
        _analisadorImagemMock.Object,
        _rateLimiterMock.Object,
        _gatewayFactoryMock.Object,
        _routerMock.Object,
        _pendencias,
        _onboardingService,
        _onboardingStore,
        _serviceProviderMock.Object,
        Mock.Of<ILogger<UpdateDispatcher>>());

    private void SetupEstrategiaCompleta()
    {
        _referenciasMock.Setup(r => r.ObterEstrategia("mrv")).Returns(new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = new FaseEstrategia
                {
                    Fase = "pos-compra",
                    CorPrincipal = "Roxo",
                    Temas = new List<string> { "Boas vindas", "Indicação premiada" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-pos-compra"] = new List<string> { "Pós Financiamento" }
                    }
                },
                ["pre-chaves"] = new FaseEstrategia
                {
                    Fase = "pre-chaves",
                    CorPrincipal = "Rosa",
                    Temas = new List<string> { "Financeiro" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-pre-chaves"] = new List<string> { "Vistoria Antecipada" }
                    }
                },
                ["pos-chaves"] = new FaseEstrategia
                {
                    Fase = "pos-chaves",
                    CorPrincipal = "Amarelo",
                    Temas = new List<string> { "Assistência técnica" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-do-produto-morador"] = new List<string> { "6 meses Pós chaves" }
                    }
                }
            }
        });
    }

    [Fact]
    public async Task CallbackQuery_DuranteOnboarding_DeveResponderEAguardarProximaPergunta()
    {
        SetupEstrategiaCompleta();
        _onboardingService.Iniciar(123, "mrv", "pedido original");

        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var callbackMessage = new Message
        {
            Id = 10,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "Qual etapa?"
        };
        var callback = new CallbackQuery
        {
            Id = "cb1",
            Data = "onb|etapa|pos-compra",
            Message = callbackMessage
        };
        var update = new Update { Id = 42, CallbackQuery = callback };

        var cts = new CancellationTokenSource();
        var pollCalls = 0;
        gatewayMock.Setup(g => g.GetUpdatesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref pollCalls) == 1)
                    return Task.FromResult(new[] { update });
                cts.Cancel();
                return Task.FromCanceled<Update[]>(cts.Token);
            });

        gatewayMock.Setup(g => g.AnswerCallbackQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatewayMock.Setup(g => g.EditMessageTextAsync(123, 10, It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        gatewayMock.Verify(g => g.AnswerCallbackQueryAsync("cb1", It.IsAny<CancellationToken>()), Times.Once);
        gatewayMock.Verify(g => g.EditMessageTextAsync(123, 10, It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()), Times.Once);

        var state = _onboardingStore.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
    }

    [Fact]
    public async Task TextoDuranteOnboarding_ComLink_DeveDefinirCtaLink()
    {
        SetupEstrategiaCompleta();
        _onboardingService.Iniciar(123, "mrv", "pedido original");
        _onboardingService.Responder(123, "onb|etapa|pos-compra");
        _onboardingService.Responder(123, "onb|marco|Pós Financiamento");
        _onboardingService.Responder(123, "onb|tema|Boas vindas");
        _onboardingService.Responder(123, "onb|objetivo|vender");

        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "https://mrv.com.br/promo"
        };
        var update = new Update { Id = 42, Message = message };

        var cts = new CancellationTokenSource();
        var pollCalls = 0;
        gatewayMock.Setup(g => g.GetUpdatesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref pollCalls) == 1)
                    return Task.FromResult(new[] { update });
                cts.Cancel();
                return Task.FromCanceled<Update[]>(cts.Token);
            });

        gatewayMock.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Id = 99 });

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        var state = _onboardingStore.Obter(123);
        state!.CtaLink.Should().Be("https://mrv.com.br/promo");
        state.PassoAtual.Should().Be(OnboardingPasso.Materiais);
    }

    [Fact]
    public async Task TextoDuranteOnboarding_NaoLink_Materiais_DeveIgnorar()
    {
        SetupEstrategiaCompleta();
        _onboardingService.Iniciar(123, "mrv", "pedido original");
        _onboardingService.Responder(123, "onb|etapa|pos-compra");
        _onboardingService.Responder(123, "onb|marco|Pós Financiamento");
        _onboardingService.Responder(123, "onb|tema|Boas vindas");
        _onboardingService.Responder(123, "onb|objetivo|vender");
        _onboardingService.Responder(123, "onb|sem-link");

        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var sentTexts = new List<string>();
        gatewayMock.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, CancellationToken>((_, text, _) => sentTexts.Add(text))
            .ReturnsAsync(new Message { Id = 99 });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "texto qualquer"
        };
        var update = new Update { Id = 42, Message = message };

        var cts = new CancellationTokenSource();
        var pollCalls = 0;
        gatewayMock.Setup(g => g.GetUpdatesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref pollCalls) == 1)
                    return Task.FromResult(new[] { update });
                cts.Cancel();
                return Task.FromCanceled<Update[]>(cts.Token);
            });

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        sentTexts.Should().Contain(s => s.Contains("Não identifiquei um link"));
        var state = _onboardingStore.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Materiais);
    }
}
