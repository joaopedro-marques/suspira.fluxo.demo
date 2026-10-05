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

        var options = Options.Create(new PreFlightOptions { MaxRodadasPerguntas = 2 });
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
            .ReturnsAsync("""{"tipo": "producao", "brief": {"canal": "email"}}""");

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
            .ReturnsAsync("""{"tipo": "producao", "brief": {"canal": "instagram"}}""");

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
