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

public class TelegramServiceAjusteTests
{
    private const long ChatId = 123;

    private readonly TelegramOptions _telegramOptions = new() { BotToken = "test-token" };
    private readonly Mock<IAnalisadorImagem> _analisadorImagemMock = new();
    private readonly Mock<ITelegramGatewayFactory> _gatewayFactoryMock = new();
    private readonly Mock<RouterService> _routerMock;
    private readonly ConversaPendenteStore _pendencias;
    private readonly OnboardingStore _onboardingStore;
    private readonly OnboardingService _onboardingService;
    private readonly EntregaStore _entregaStore;
    private readonly Mock<RefinadorService> _refinadorMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<IReferenciasCliente> _referenciasMock = new();
    private readonly AjustesOptions _ajustesOptions = new() { MaxRodadas = 3, MaxRefacoesQa = 4 };

    public TelegramServiceAjusteTests()
    {
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
            TestOptions.Create(_ajustesOptions),
            TimeProvider.System,
            Mock.Of<ILogger<EntregaStore>>());

        _refinadorMock = new Mock<RefinadorService>(
            new AgenteDefinicao("refinador", "test/model", 0.2, 2000, "persona"),
            Mock.Of<IServicoChat>(),
            Mock.Of<ITemplateCatalogo>(),
            Mock.Of<ILogger<RefinadorService>>());
    }

    private TelegramService CriarService() => new(
        Mock.Of<ILogger<TelegramService>>(),
        TestOptions.Create(_telegramOptions),
        TestOptions.Create(new ConcorrenciaOptions()),
        TestOptions.Create(new PreFlightOptions()),
        _analisadorImagemMock.Object,
        new RateLimiterService(TestOptions.Create(new SegurancaOptions())),
        _gatewayFactoryMock.Object,
        _routerMock.Object,
        _pendencias,
        _onboardingService,
        _onboardingStore,
        _entregaStore,
        _refinadorMock.Object,
        TestOptions.Create(_ajustesOptions),
        _serviceProviderMock.Object,
        Mock.Of<ILogger<UpdateDispatcher>>());

    private EstadoEntrega SeedEntrega()
    {
        var contexto = new PipelineContext
        {
            ChatId = ChatId,
            Cliente = "mrv",
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", "engajar", null, null, null, null, new(), new())
        };
        contexto.Resultado.RespostaFinal = "<html>v1</html>";

        var entrega = new EstadoEntrega { Contexto = contexto };
        _entregaStore.Guardar(ChatId, entrega);
        return entrega;
    }

    private (Mock<ITelegramGateway> Gateway, CancellationTokenSource Cts) ConfigurarGateway(params Update[] updates)
    {
        var gateway = new Mock<ITelegramGateway>();
        _gatewayFactoryMock.Setup(f => f.Create(_telegramOptions.BotToken)).Returns(gateway.Object);

        gateway.Setup(g => g.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 1, Username = "bot" });
        gateway.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Id = 99 });
        gateway.Setup(g => g.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Id = 99 });
        gateway.Setup(g => g.AnswerCallbackQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gateway.Setup(g => g.EditMessageTextAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gateway.Setup(g => g.EditMessageTextAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gateway.Setup(g => g.SendDocumentAsync(It.IsAny<long>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var pollCalls = 0;
        gateway.Setup(g => g.GetUpdatesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref pollCalls) == 1)
                    return Task.FromResult(updates);
                cts.Cancel();
                return Task.FromCanceled<Update[]>(cts.Token);
            });

        return (gateway, cts);
    }

    private async Task RodarAsync(Mock<ITelegramGateway> gateway, CancellationTokenSource cts)
    {
        var service = CriarService();
        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    private static Update CallbackUpdate(string data, int messageId = 10)
    {
        return new Update
        {
            Id = 1,
            CallbackQuery = new CallbackQuery
            {
                Id = "cb1",
                Data = data,
                Message = new Message { Id = messageId, Chat = new Chat { Id = ChatId, Type = ChatType.Private } }
            }
        };
    }

    private static Update TextUpdate(string texto)
    {
        return new Update
        {
            Id = 2,
            Message = new Message
            {
                Id = 2,
                Chat = new Chat { Id = ChatId, Type = ChatType.Private },
                Text = texto
            }
        };
    }

    private static bool TemBotao(InlineKeyboardMarkup? keyboard, string callbackData)
    {
        return keyboard != null &&
               keyboard.InlineKeyboard.Any(row => row.Any(b => b.CallbackData == callbackData));
    }

    private void ConfigurarPipeline(ResultadoPipeline resultado, Action<PipelineContext>? onExecutar = null)
    {
        var runner = new Mock<PipelineRunner>(Mock.Of<ILogger<PipelineRunner>>());
        runner.Setup(r => r.ExecutarAjusteAsync(
                It.IsAny<PipelineContext>(),
                It.IsAny<IReadOnlyList<IPipelineStep>>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<PipelineContext, IReadOnlyList<IPipelineStep>, string, int, CancellationToken>(
                (ctx, _, _, _, _) => onExecutar?.Invoke(ctx))
            .ReturnsAsync(resultado);

        var pipeline = new Mock<PipelineEmail>(
            Mock.Of<IServicoChat>(),
            Mock.Of<IGeradorImagem>(),
            _referenciasMock.Object,
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
        pipeline.Setup(p => p.CriarSteps()).Returns(new List<IPipelineStep>());

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineRunner))).Returns(runner.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(PipelineEmail))).Returns(pipeline.Object);
    }

    [Fact]
    public async Task PedirAjustes_DeveAguardarFeedback()
    {
        SeedEntrega();
        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.PedirAjustes));

        await RodarAsync(gateway, cts);

        var entrega = _entregaStore.Obter(ChatId);
        entrega!.Fase.Should().Be(FaseAjuste.AguardandoFeedback);
        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("Descreva")),
            It.IsAny<InlineKeyboardMarkup?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PedirAjustes_NoLimiteDeRodadas_DeveRecusar()
    {
        var entrega = SeedEntrega();
        entrega.RodadasAjuste = 3;
        _entregaStore.Guardar(ChatId, entrega);

        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.PedirAjustes));

        await RodarAsync(gateway, cts);

        _entregaStore.Obter(ChatId)!.Fase.Should().Be(FaseAjuste.AposEntrega);
        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("limite")),
            It.IsAny<InlineKeyboardMarkup?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FeedbackTexto_DeveClassificarEPedirConfirmacao()
    {
        SeedEntrega();
        _refinadorMock
            .Setup(r => r.ClassificarAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanoAjuste("copy", "Deixe o titulo mais direto", "o titulo ficou formal"));

        var (gateway, cts) = ConfigurarGateway(
            CallbackUpdate(AjusteCallbacks.PedirAjustes),
            TextUpdate("o titulo ficou muito formal"));

        await RodarAsync(gateway, cts);

        var entrega = _entregaStore.Obter(ChatId);
        entrega!.Fase.Should().Be(FaseAjuste.AguardandoConfirmacao);
        entrega.Plano.Should().NotBeNull();
        entrega.Plano!.StepAlvo.Should().Be("copy");

        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("Ajuste identificado")),
            It.Is<InlineKeyboardMarkup>(k => TemBotao(k, AjusteCallbacks.AplicarAjuste)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FeedbackTexto_ClassificacaoInvalida_DevePedirDetalhamento()
    {
        SeedEntrega();
        var entrega = _entregaStore.Obter(ChatId)!;
        entrega.Fase = FaseAjuste.AguardandoFeedback;
        _entregaStore.Guardar(ChatId, entrega);

        _refinadorMock
            .Setup(r => r.ClassificarAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanoAjuste?)null);

        var (gateway, cts) = ConfigurarGateway(TextUpdate("sei la"));

        await RodarAsync(gateway, cts);

        _entregaStore.Obter(ChatId)!.Fase.Should().Be(FaseAjuste.AguardandoFeedback);
        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("Não consegui identificar")),
            It.IsAny<InlineKeyboardMarkup?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AplicarAjuste_DeveExecutarEtapaAlvoEDeliverarNovaVersao()
    {
        var entrega = SeedEntrega();
        entrega.Fase = FaseAjuste.AguardandoConfirmacao;
        entrega.Plano = new PlanoAjuste("diagramacao", "Troque o fundo das secoes", "fundo ruim");
        _entregaStore.Guardar(ChatId, entrega);

        PipelineContext? contextoExecutado = null;
        ConfigurarPipeline(
            new ResultadoPipeline { QaAprovado = true, RespostaFinal = "<html>v2</html>" },
            ctx => contextoExecutado = ctx);

        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.AplicarAjuste));

        await RodarAsync(gateway, cts);

        contextoExecutado.Should().NotBeNull();
        contextoExecutado!.FeedbackAjuste.Should().Contain("fundo ruim");

        var atualizada = _entregaStore.Obter(ChatId)!;
        atualizada.Versao.Should().Be(2);
        atualizada.RodadasAjuste.Should().Be(1);
        atualizada.Fase.Should().Be(FaseAjuste.AposEntrega);
        atualizada.Contexto.Resultado.RespostaFinal.Should().Be("<html>v2</html>");

        gateway.Verify(g => g.SendDocumentAsync(
            ChatId,
            It.IsAny<Stream>(),
            "entregavel_v2.zip",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AplicarAjuste_ComTemplateId_DeveTrocarTemplate()
    {
        var entrega = SeedEntrega();
        entrega.Fase = FaseAjuste.AguardandoConfirmacao;
        entrega.Plano = new PlanoAjuste("template", "Use outro template", "layout errado", "MRV_html_limite_vistoria");
        _entregaStore.Guardar(ChatId, entrega);

        PipelineContext? contextoExecutado = null;
        ConfigurarPipeline(
            new ResultadoPipeline { QaAprovado = true },
            ctx => contextoExecutado = ctx);

        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.AplicarAjuste));

        await RodarAsync(gateway, cts);

        contextoExecutado!.TemplateId.Should().Be("MRV_html_limite_vistoria");
    }

    [Fact]
    public async Task AplicarAjuste_QaReprova_DeveManterVersaoAnterior()
    {
        var entrega = SeedEntrega();
        entrega.Fase = FaseAjuste.AguardandoConfirmacao;
        entrega.Plano = new PlanoAjuste("copy", "Reescreva o titulo", "titulo ruim");
        _entregaStore.Guardar(ChatId, entrega);

        ConfigurarPipeline(new ResultadoPipeline
        {
            QaAprovado = false,
            QaFeedbackFinal = "Ainda nao atende"
        });

        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.AplicarAjuste));

        await RodarAsync(gateway, cts);

        var atualizada = _entregaStore.Obter(ChatId)!;
        atualizada.Versao.Should().Be(1);
        atualizada.RodadasAjuste.Should().Be(0);
        atualizada.Contexto.Resultado.RespostaFinal.Should().Be("<html>v1</html>");
        atualizada.Plano.Should().BeNull();

        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("continua valendo")),
            It.IsAny<InlineKeyboardMarkup?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AprovarEntrega_DeveMarcarComoAprovada()
    {
        SeedEntrega();
        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.AprovarEntrega));

        await RodarAsync(gateway, cts);

        var entrega = _entregaStore.Obter(ChatId)!;
        entrega.Aprovada.Should().BeTrue();
        entrega.Fase.Should().Be(FaseAjuste.AposEntrega);
    }

    [Fact]
    public async Task CancelarAjuste_DeveLimparPlano()
    {
        var entrega = SeedEntrega();
        entrega.Fase = FaseAjuste.AguardandoConfirmacao;
        entrega.Plano = new PlanoAjuste("copy", "x", "y");
        _entregaStore.Guardar(ChatId, entrega);

        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.CancelarAjuste));

        await RodarAsync(gateway, cts);

        var atualizada = _entregaStore.Obter(ChatId)!;
        atualizada.Fase.Should().Be(FaseAjuste.AposEntrega);
        atualizada.Plano.Should().BeNull();
    }

    [Fact]
    public async Task CallbackAjuste_SemEntrega_DeveInformar()
    {
        var (gateway, cts) = ConfigurarGateway(CallbackUpdate(AjusteCallbacks.AplicarAjuste));

        await RodarAsync(gateway, cts);

        gateway.Verify(g => g.EditMessageTextAsync(
            ChatId,
            It.IsAny<int>(),
            It.Is<string>(t => t.Contains("expirou")),
            It.IsAny<InlineKeyboardMarkup?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
