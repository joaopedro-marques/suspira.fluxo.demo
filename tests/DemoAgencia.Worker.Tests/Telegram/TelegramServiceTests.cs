using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
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

        _pendencias.Guardar(123, new EstadoPreFlight { ChatId = 123, MensagemOriginal = "pedido original" });
        _routerMock.Setup(r => r.ResumirAsync(123, "resposta", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("429"));

        var service = new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

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

        _pendencias.Guardar(123, new EstadoPreFlight { ChatId = 123, MensagemOriginal = "pedido original" });

        var brief = new Brief("email", null, null, null, null, null, new(), new());
        var resultado = new RouterResultado("producao", null, new(), "mrv", brief);
        _routerMock.Setup(r => r.ResumirAsync(123, "resposta", It.IsAny<CancellationToken>()))
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
            "<tr></tr>",
            "<tr></tr>");

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineEmail))).Returns(pipelineEmail);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineRunner))).Returns(runnerMock.Object);

        var service = new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

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

        var service = new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

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
        var resultado = new RouterResultado("producao", null, new(), "mrv", brief);
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
            "<tr></tr>",
            "<tr></tr>");

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineEmail))).Returns(pipelineEmail);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineRunner))).Returns(runnerMock.Object);

        var service = new TelegramService(
            _loggerMock.Object,
            TestOptions.Create(_telegramOptions),
            _analisadorImagemMock.Object,
            _rateLimiterMock.Object,
            _gatewayFactoryMock.Object,
            _routerMock.Object,
            _pendencias,
            _serviceProviderMock.Object);

        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        gatewayMock.Verify(g => g.SendDocumentAsync(It.IsAny<long>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        editCalls.Should().Contain(m => m.Contains("QA nao aprovou"));
    }
}
