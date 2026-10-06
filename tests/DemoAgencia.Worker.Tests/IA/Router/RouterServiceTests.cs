using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Router;

public class RouterServiceTests
{
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<IServicoChat> _chatMock;
    private readonly IAgentesCatalogo _catalogo;
    private readonly ConversaPendenteStore _store;
    private readonly RouterService _service;

    public RouterServiceTests()
    {
        _refsMock = new Mock<IReferenciasCliente>();
        _chatMock = new Mock<IServicoChat>();

        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme", "beta" });

        var timeProvider = new FakeTimeProvider();
        var storeLogger = new Mock<ILogger<ConversaPendenteStore>>();
        _store = new ConversaPendenteStore(Options.Create(new PreFlightOptions()), timeProvider, storeLogger.Object);

        _catalogo = new StubCatalogo(new AgenteDefinicao(
            "router", "deepseek/deepseek-v3.2", 0.2, 2000, "Persona do router"));

        var options = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "acme", "beta" },
            CanaisPermitidos = new List<string> { "email", "instagram", "landing" }
        });
        var logger = new Mock<ILogger<RouterService>>();
        _service = new RouterService(_refsMock.Object, _chatMock.Object, _catalogo, _store, options, logger.Object);
    }

    [Fact]
    public async Task IniciarAsync_WithConversa_ShouldReturnConversa()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "conversa", "resposta": "O resultado foi bom"}""");

        var resultado = await _service.IniciarAsync(123, "Ola, como foi a campanha?");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("conversa");
        resultado.Resposta.Should().Be("O resultado foi bom");
    }

    [Fact]
    public async Task IniciarAsync_WithClientDetected_ShouldNormalizeCliente()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "ACME", "brief": {"canal": "email"}}""");

        var resultado = await _service.IniciarAsync(123, "criar email para a Acme");

        resultado.Should().NotBeNull();
        resultado!.Cliente.Should().Be("acme");
    }

    [Fact]
    public async Task IniciarAsync_WithEsclarecimento_ShouldStorePending()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Qual o publico?"]}""");

        var resultado = await _service.IniciarAsync(123, "criar post");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("esclarecimento");
        _store.Obter(123).Should().NotBeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithProducao_ShouldClearPending()
    {
        var estado = new EstadoPreFlight { ChatId = 123, MensagemOriginal = "prev" };
        _store.Guardar(123, estado);

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "acme", "brief": {"canal": "email"}}""");

        var resultado = await _service.IniciarAsync(123, "criar email");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("producao");
        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithInvalidJson_ShouldRetryOnce()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Texto sem JSON")
            .ReturnsAsync("""{"tipo": "conversa", "resposta": "ok"}""");

        var resultado = await _service.IniciarAsync(123, "ola");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("conversa");
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task IniciarAsync_WithTwoInvalidJsons_ShouldReturnNull()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Texto invalido");

        var resultado = await _service.IniciarAsync(123, "ola");

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task ResumirAsync_WithNoPending_ShouldReturnNull()
    {
        var resultado = await _service.ResumirAsync(123, "minha resposta");

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task ResumirAsync_WithPending_ShouldAccumulateAndCallRouter()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Qual o publico?"]}""");

        await _service.IniciarAsync(123, "criar post");

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "beta", "brief": {"canal": "instagram"}}""");

        var resultado = await _service.ResumirAsync(123, "jovens");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("producao");
        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public async Task ResumirAsync_WhenMaxRoundsExceeded_ShouldClearAndReturnNull()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Q1"]}""");

        await _service.IniciarAsync(123, "msg1");
        await _service.ResumirAsync(123, "r1");

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Q2"]}""");

        var resultado = await _service.ResumirAsync(123, "r2");

        resultado.Should().BeNull();
        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithEstrategiaCliente_ShouldInjectFasesNoPrompt()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv" });
        var estrategia = new EstrategiaCliente { Cliente = "mrv" };
        estrategia.Fases["pos-compra"] = new FaseEstrategia
        {
            Fase = "pos-compra",
            Temas = new List<string> { "Boas vindas", "Financeiro" },
            SubJornadas = new Dictionary<string, List<string>>
            {
                ["Jornada-pos-compra"] = new List<string> { "Pos Financiamento" }
            }
        };
        estrategia.Fases["pre-chaves"] = new FaseEstrategia { Fase = "pre-chaves" };
        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);

        var mrvOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var mrvService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, mrvOptions,
            new Mock<ILogger<RouterService>>().Object);

        string? promptCapturado = null;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (chatId, persona, modelo, prompt, etapa, temp, maxTok, ct) => promptCapturado = prompt)
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Qual etapa da jornada?"]}""");

        await mrvService.IniciarAsync(123, "criar email para MRV");

        promptCapturado.Should().NotBeNull();
        promptCapturado.Should().Contain("pos-compra");
        promptCapturado.Should().Contain("pre-chaves");
    }

    [Fact]
    public async Task IniciarAsync_WithoutEstrategia_ShouldNotInjectEstrategiaNoPrompt()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme" });
        _refsMock.Setup(r => r.ObterEstrategia("acme")).Returns((EstrategiaCliente?)null);

        string? promptCapturado = null;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (chatId, persona, modelo, prompt, etapa, temp, maxTok, ct) => promptCapturado = prompt)
            .ReturnsAsync("""{"tipo": "producao", "brief": {"canal": "email"}}""");

        await _service.IniciarAsync(123, "criar email para acme");

        promptCapturado.Should().NotBeNull();
        promptCapturado.Should().NotContain("Estrategia");
    }

    [Fact]
    public async Task Guarda_WhenProducaoClienteNaoPermitido_ShouldCoerceToForaContexto()
    {
        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "acme", "brief": {"canal": "email"}}""");

        var resultado = await restrictedService.IniciarAsync(123, "criar email para acme");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("fora_contexto");
        resultado.Motivo.Should().Be(RouterResultado.Motivos.ClienteNaoPermitido);
        resultado.Brief.Should().BeNull();
    }

    [Fact]
    public async Task Guarda_WhenProducaoCanalNaoPermitido_ShouldCoerceToForaContexto()
    {
        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "acme" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "acme", "brief": {"canal": "instagram"}}""");

        var resultado = await restrictedService.IniciarAsync(123, "criar post para acme");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("fora_contexto");
        resultado.Motivo.Should().Be(RouterResultado.Motivos.CanalNaoPermitido);
    }

    [Fact]
    public async Task Guarda_WhenProducaoSemCliente_ShouldCoerceToForaContexto()
    {
        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "brief": {"canal": "email"}}""");

        var resultado = await restrictedService.IniciarAsync(123, "criar email de boas-vindas");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("fora_contexto");
        resultado.Motivo.Should().Be(RouterResultado.Motivos.ClienteNaoPermitido);
    }

    [Fact]
    public async Task Guarda_WhenProducaoPermitido_ShouldPassThrough()
    {
        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tipo": "producao", "cliente": "mrv", "brief": {"canal": "email", "objetivo": "vender"}}""");

        var resultado = await restrictedService.IniciarAsync(123, "criar email para MRV");

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("producao");
        resultado.Cliente.Should().Be("mrv");
        resultado.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task MontaPrompt_ShouldInjectOnlyPermittedClientsAndChannels()
    {
        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv", "acme" });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        string? promptCapturado = null;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (chatId, persona, modelo, prompt, etapa, temp, maxTok, ct) => promptCapturado = prompt)
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Qual o publico?"]}""");

        await restrictedService.IniciarAsync(123, "criar email para mrv");

        promptCapturado.Should().NotBeNull();
        promptCapturado.Should().Contain("Clientes atendidos");
        promptCapturado.Should().Contain("mrv");
        promptCapturado.Should().NotContain("acme");
        promptCapturado.Should().Contain("Canais atendidos");
        promptCapturado.Should().Contain("email");
    }

    [Fact]
    public async Task MontaPrompt_ShouldNotInjectEstrategiaForNonPermittedClient()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv", "acme" });
        var estrategiaAcme = new EstrategiaCliente { Cliente = "acme" };
        estrategiaAcme.Fases["pos-compra"] = new FaseEstrategia { Fase = "pos-compra" };
        _refsMock.Setup(r => r.ObterEstrategia("acme")).Returns(estrategiaAcme);

        var restrictedOptions = Options.Create(new PreFlightOptions
        {
            MaxRodadasPerguntas = 2,
            ClientesPermitidos = new List<string> { "mrv" },
            CanaisPermitidos = new List<string> { "email" }
        });
        var restrictedService = new RouterService(
            _refsMock.Object, _chatMock.Object, _catalogo, _store, restrictedOptions,
            new Mock<ILogger<RouterService>>().Object);

        string? promptCapturado = null;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "router", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (chatId, persona, modelo, prompt, etapa, temp, maxTok, ct) => promptCapturado = prompt)
            .ReturnsAsync("""{"tipo": "esclarecimento", "perguntas": ["Qual o cliente?"]}""");

        await restrictedService.IniciarAsync(123, "criar email para acme");

        promptCapturado.Should().NotBeNull();
        promptCapturado.Should().NotContain("Estrategia do cliente");
    }

    private class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private class StubCatalogo : IAgentesCatalogo
    {
        private readonly Dictionary<string, AgenteDefinicao> _agentes;

        public StubCatalogo(params AgenteDefinicao[] agentes)
        {
            _agentes = agentes.ToDictionary(a => a.Nome, StringComparer.OrdinalIgnoreCase);
        }

        public AgenteDefinicao Obter(string nome) => _agentes[nome];
    }
}
