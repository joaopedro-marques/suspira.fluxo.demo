using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ajustes;
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

public class TelegramServiceTests
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
    private readonly EntregaStore _entregaStore;
    private readonly Mock<RefinadorService> _refinadorMock;
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
            Mock.Of<IAgentesCatalogo>(),
            new ConversaPendenteStore(
                TestOptions.Create(new PreFlightOptions()),
                TimeProvider.System,
                Mock.Of<ILogger<ConversaPendenteStore>>()),
            TestOptions.Create(new PreFlightOptions()),
            Mock.Of<ILogger<RouterService>>(),
            new BriefExtractor());

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

        _entregaStore = new EntregaStore(
            TestOptions.Create(new AjustesOptions()),
            TimeProvider.System,
            Mock.Of<ILogger<EntregaStore>>());

        _refinadorMock = new Mock<RefinadorService>(
            new AgenteDefinicao("refinador", "deepseek/deepseek-v3.2", 0.2, 2000, "persona"),
            Mock.Of<IServicoChat>(),
            Mock.Of<ITemplateCatalogo>(),
            Mock.Of<ILogger<RefinadorService>>());

        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    private TelegramService CriarService() => CriarService(new PreFlightOptions());

    private TelegramService CriarService(PreFlightOptions preFlightOptions) => new(
        _loggerMock.Object,
        TestOptions.Create(_telegramOptions),
        TestOptions.Create(new ConcorrenciaOptions()),
        TestOptions.Create(preFlightOptions),
        _analisadorImagemMock.Object,
        _rateLimiterMock.Object,
        _gatewayFactoryMock.Object,
        _routerMock.Object,
        _pendencias,
        _onboardingService,
        _onboardingStore,
        _entregaStore,
        _refinadorMock.Object,
        TestOptions.Create(new AjustesOptions()),
        _serviceProviderMock.Object,
        Mock.Of<ILogger<UpdateDispatcher>>());

    [Fact]
    public void Constructor_WithValidToken_ShouldNotThrow()
    {
        var act = () => CriarService();

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithEmptyToken_ShouldNotThrow()
    {
        _telegramOptions.BotToken = "";

        var act = () => CriarService();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_RetomadaComErroNoRouter_DeveEditarMensagemComErro()
    {
        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "resposta"
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

        var errorEditCalled = new TaskCompletionSource<bool>();
        gatewayMock.Setup(g => g.EditMessageTextAsync(123, 99,
            "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
            It.IsAny<CancellationToken>()))
            .Callback(() => errorEditCalled.TrySetResult(true))
            .Returns(Task.CompletedTask);

        _routerMock.Setup(r => r.IniciarAsync(123, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("429"));

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await errorEditCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        gatewayMock.Verify(g => g.EditMessageTextAsync(123, 99,
            "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RetomadaComErroNoPipeline_DeveEditarProduzindoEDepoisErro()
    {
        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "resposta"
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

        var editCalls = new List<string>();
        var errorEditCalled = new TaskCompletionSource<bool>();
        gatewayMock.Setup(g => g.EditMessageTextAsync(123, 99, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, string, CancellationToken>((_, _, text, _) =>
            {
                editCalls.Add(text);
                if (text.Contains("Desculpe"))
                    errorEditCalled.TrySetResult(true);
            })
            .Returns(Task.CompletedTask);

        var brief = new Brief("email", null, null, null, null, null, new(), new());
        var resultado = new RouterResultado("producao", null, "mrv", brief);
        _routerMock.Setup(r => r.IniciarAsync(123, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);

        var runnerMock = new Mock<PipelineRunner>(Mock.Of<ILogger<PipelineRunner>>());
        runnerMock.Setup(r => r.ExecutarAsync(It.IsAny<PipelineContext>(), It.IsAny<IReadOnlyList<IPipelineStep>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("429"));

        var pipelineEmail = new PipelineEmail(
            Mock.Of<IServicoChat>(),
            Mock.Of<IGeradorImagem>(),
            Mock.Of<IReferenciasCliente>(),
            Mock.Of<IAnalisadorImagem>(),
            Mock.Of<IAgentesCatalogo>(),
            Mock.Of<ITemplateCatalogo>(),
            Mock.Of<IBannerDescricaoCache>(),
            Mock.Of<IIconDescricaoCache>(),
            Mock.Of<ILogger<StepCopyEmail>>(),
            Mock.Of<ILogger<StepImagemHero>>(),
            Mock.Of<ILogger<StepDiagramacaoEmail>>(),
            Mock.Of<ILogger<StepMapaEmocionalEmail>>(),
            Mock.Of<ILogger<StepAssetsEmail>>(),
            Mock.Of<ILogger<StepTemplateEmail>>());

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineEmail))).Returns(pipelineEmail);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineRunner))).Returns(runnerMock.Object);

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await errorEditCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        editCalls.Should().Contain("🚀 Produzindo...");
        editCalls.Should().Contain("Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.");
    }

    [Fact]
    public async Task ExecuteAsync_SemPendenciaComErroNoRouter_DeveEditarMensagemComErro()
    {
        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "pedido"
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

        var errorEditCalled = new TaskCompletionSource<bool>();
        gatewayMock.Setup(g => g.EditMessageTextAsync(123, 99,
            "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
            It.IsAny<CancellationToken>()))
            .Callback(() => errorEditCalled.TrySetResult(true))
            .Returns(Task.CompletedTask);

        _routerMock.Setup(r => r.IniciarAsync(123, "pedido", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("429"));

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await errorEditCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        gatewayMock.Verify(g => g.EditMessageTextAsync(123, 99,
            "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_QaReproved_ShouldNotSendZipAndShowFeedback()
    {
        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "pedido"
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

        var editCalls = new List<string>();
        gatewayMock.Setup(g => g.EditMessageTextAsync(123, 99, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, string, CancellationToken>((_, _, text, _) => editCalls.Add(text))
            .Returns(Task.CompletedTask);

        var brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>());
        var resultado = new RouterResultado("producao", null, "mrv", brief);
        _routerMock.Setup(r => r.IniciarAsync(123, "pedido", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);

        var resultadoPipeline = new ResultadoPipeline
        {
            RespostaFinal = "<html>email</html>",
            QaAprovado = false,
            QaFeedbackFinal = "QA nao aprovou"
        };
        var runnerMock = new Mock<PipelineRunner>(Mock.Of<ILogger<PipelineRunner>>());
        runnerMock.Setup(r => r.ExecutarAsync(It.IsAny<PipelineContext>(), It.IsAny<IReadOnlyList<IPipelineStep>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultadoPipeline);

        var pipelineEmail = new PipelineEmail(
            Mock.Of<IServicoChat>(),
            Mock.Of<IGeradorImagem>(),
            Mock.Of<IReferenciasCliente>(),
            Mock.Of<IAnalisadorImagem>(),
            Mock.Of<IAgentesCatalogo>(),
            Mock.Of<ITemplateCatalogo>(),
            Mock.Of<IBannerDescricaoCache>(),
            Mock.Of<IIconDescricaoCache>(),
            Mock.Of<ILogger<StepCopyEmail>>(),
            Mock.Of<ILogger<StepImagemHero>>(),
            Mock.Of<ILogger<StepDiagramacaoEmail>>(),
            Mock.Of<ILogger<StepMapaEmocionalEmail>>(),
            Mock.Of<ILogger<StepAssetsEmail>>(),
            Mock.Of<ILogger<StepTemplateEmail>>());

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineEmail))).Returns(pipelineEmail);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineRunner))).Returns(runnerMock.Object);

        var service = CriarService();

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        gatewayMock.Verify(g => g.SendDocumentAsync(It.IsAny<long>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        editCalls.Should().Contain(m => m.Contains("QA nao aprovou"));
    }

    [Fact]
    public async Task HandleCommand_Help_WithClientesPermitidos_ShouldUseDynamicList()
    {
        var preFlightOptions = new PreFlightOptions
        {
            ClientesPermitidos = new List<string> { "acme", "beta" }
        };
        var service = CriarService(preFlightOptions);

        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var sentMessages = new List<string>();
        gatewayMock.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, CancellationToken>((_, text, _) => sentMessages.Add(text))
            .ReturnsAsync(new Message { Id = 1 });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "/help"
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

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        sentMessages.Should().ContainSingle(m => m.Contains("acme, beta") && m.Contains("Crie um email para a acme"));
    }

    [Fact]
    public async Task HandleCommand_Help_WithEmptyClientesPermitidos_ShouldUseGenericMessage()
    {
        var preFlightOptions = new PreFlightOptions
        {
            ClientesPermitidos = new List<string>()
        };
        var service = CriarService(preFlightOptions);

        var gatewayMock = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(gatewayMock.Object);
        gatewayMock.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });

        var sentMessages = new List<string>();
        gatewayMock.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, CancellationToken>((_, text, _) => sentMessages.Add(text))
            .ReturnsAsync(new Message { Id = 1 });

        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = 123, Type = ChatType.Private },
            Text = "/help"
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

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        sentMessages.Should().ContainSingle(m => m.Contains("os clientes configurados") && m.Contains("Crie um email para a CLIENTE"));
    }
}
