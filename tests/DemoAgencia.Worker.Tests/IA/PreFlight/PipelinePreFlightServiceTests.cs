using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.PreFlight;

public class PipelinePreFlightServiceTests
{
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<IAgentesCatalogo> _agentesMock;
    private readonly Mock<IServicoChat> _chatMock;
    private readonly Mock<ILogger<EnriquecedorContextoCliente>> _enriquecedorLoggerMock;
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly ConversaPendenteStore _store;
    private readonly PipelinePreFlightService _service;

    private static AgenteDefinicao CriarAgente(string nome, string papel = "preflight") => new()
    {
        Nome = nome,
        Papel = papel,
        ModeloAlvo = "test-model",
        Persona = $"Persona de {nome}",
        Interno = true
    };

    public PipelinePreFlightServiceTests()
    {
        _refsMock = new Mock<IReferenciasCliente>();
        _agentesMock = new Mock<IAgentesCatalogo>();
        _chatMock = new Mock<IServicoChat>();
        _enriquecedorLoggerMock = new Mock<ILogger<EnriquecedorContextoCliente>>();
        _analisadorMock = new Mock<IAnalisadorImagem>();

        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme", "beta" });
        _refsMock.Setup(r => r.ObterReferenciasTexto(It.IsAny<string>())).Returns("");
        _refsMock.Setup(r => r.ListarImagens(It.IsAny<string>())).Returns(new List<string>());
        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>
        {
            new() { Id = "asset_1", Cliente = "acme", Tipo = TipoAsset.Logo, Nome = "principal" },
            new() { Id = "asset_2", Cliente = "acme", Tipo = TipoAsset.Header, Nome = "email" }
        });

        _agentesMock.Setup(a => a.ObterPorNome("Refinador")).Returns(CriarAgente("Refinador"));
        _agentesMock.Setup(a => a.ObterPorNome("Montador de Briefing")).Returns(CriarAgente("Montador de Briefing"));

        var enriquecedor = new EnriquecedorContextoCliente(_refsMock.Object, _analisadorMock.Object, _enriquecedorLoggerMock.Object);
        var timeProvider = new FakeTimeProvider();
        var storeLogger = new Mock<ILogger<ConversaPendenteStore>>();
        _store = new ConversaPendenteStore(Options.Create(new PreFlightOptions()), timeProvider, storeLogger.Object);

        var options = Options.Create(new PreFlightOptions { MaxRodadasPerguntas = 2 });
        var logger = new Mock<ILogger<PipelinePreFlightService>>();
        _service = new PipelinePreFlightService(
            _refsMock.Object, _agentesMock.Object, _chatMock.Object,
            enriquecedor, _store, options, logger.Object);
    }

    [Fact]
    public async Task IniciarAsync_WithSimpleQuestion_ShouldReturnConcluidoWithOriginalMessage()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Ola", "simples": true}""");

        var resultado = await _service.IniciarAsync(123, "Ola, tudo bem?");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Briefing.Should().Be("Ola, tudo bem?");
        resultado.Perguntas.Should().BeNull();
    }

    [Fact]
    public async Task IniciarAsync_WhenNeedsClarification_ShouldReturnPerguntas()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": true, "perguntas": ["Qual o publico?"]}""");

        var resultado = await _service.IniciarAsync(123, "criar post");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.PrecisaEsclarecimento);
        resultado.Perguntas.Should().Contain("Qual o publico?");
    }

    [Fact]
    public async Task IniciarAsync_WhenNeedsClarification_ShouldStorePendingState()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": true, "perguntas": ["Qual o publico?"]}""");

        await _service.IniciarAsync(123, "criar post");

        _store.Obter(123).Should().NotBeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithClientIdentified_ShouldLoadContextAndReturnBriefing()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Acme", "cliente": "acme", "simples": false}""")
            .ReturnsAsync("""{"briefing": "Criar post para Acme no Instagram", "assets_reservados": ["asset_1"]}""");

        var resultado = await _service.IniciarAsync(123, "criar post para a acme");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Briefing.Should().Be("Criar post para Acme no Instagram");
        resultado.Cliente.Should().Be("acme");
    }

    [Fact]
    public async Task IniciarAsync_WithInvalidRefinadorJson_ShouldReturnFalha()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Texto sem JSON");

        var resultado = await _service.IniciarAsync(123, "criar post");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Falha);
    }

    [Fact]
    public async Task IniciarAsync_WithInvalidMontadorJson_ShouldReturnFalha()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post", "simples": false}""")
            .ReturnsAsync("Texto sem JSON")
            .ReturnsAsync("Ainda sem JSON valido");

        var resultado = await _service.IniciarAsync(123, "criar post");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Falha);
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "preflight_montador_retry", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IniciarAsync_WithMontadorFailureAndRetrySuccess_ShouldReturnConcluido()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Acme", "simples": false}""")
            .ReturnsAsync("resposta invalida sem json")
            .ReturnsAsync("""{"briefing": "Briefing via retry", "assets_reservados": ["asset_1"]}""");

        var resultado = await _service.IniciarAsync(123, "post para acme");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Briefing.Should().Be("Briefing via retry");
    }

    [Fact]
    public async Task IniciarAsync_WithMissingAgents_ShouldReturnFalha()
    {
        _agentesMock.Setup(a => a.ObterPorNome("Refinador")).Returns((AgenteDefinicao?)null);

        var resultado = await _service.IniciarAsync(123, "criar post");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Falha);
    }

    [Fact]
    public async Task ResumirAsync_WithNoPendingState_ShouldReturnFalha()
    {
        var resultado = await _service.ResumirAsync(123, "resposta");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Falha);
    }

    [Fact]
    public async Task ResumirAsync_WithRefinedAnswer_ShouldReturnConcluido()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": true, "perguntas": ["Qual o publico?"]}""")
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para jovens", "simples": false}""")
            .ReturnsAsync("""{"briefing": "Criar post para jovens", "assets_reservados": []}""");

        var resultado1 = await _service.IniciarAsync(123, "criar post");
        resultado1.Tipo.Should().Be(TipoResultadoPreFlight.PrecisaEsclarecimento);

        var resultado2 = await _service.ResumirAsync(123, "jovens 18-25");
        resultado2.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado2.Briefing.Should().Be("Criar post para jovens");
    }

    [Fact]
    public async Task ResumirAsync_ShouldRemovePendingStateAfterCompletion()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": true, "perguntas": ["Q?"]}""")
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "ok", "simples": false}""")
            .ReturnsAsync("""{"briefing": "b", "assets_reservados": []}""");

        await _service.IniciarAsync(123, "msg");
        await _service.ResumirAsync(123, "resp");

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithNoClientAndSimpleRequest_ShouldReturnBriefing()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Ola", "simples": true}""");

        var resultado = await _service.IniciarAsync(123, "Ola");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Cliente.Should().BeNull();
    }

    [Fact]
    public async Task IniciarAsync_WithUnregisteredClient_ShouldReturnPrecisaEsclarecimento()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme" });
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Enel", "cliente": "enel", "simples": false}""");

        var resultado = await _service.IniciarAsync(123, "post para enel");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.PrecisaEsclarecimento);
        resultado.Perguntas.Should().ContainSingle(p => p.Contains("enel"));
        _enriquecedorLoggerMock.Object.GetType();
        _refsMock.Verify(r => r.ObterReferenciasTexto("enel"), Times.Never);
    }

    [Fact]
    public async Task ResumirAsync_WithStillUnregisteredClient_ShouldReturnBloqueado()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme" });
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Enel", "cliente": "enel", "simples": false}""")
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Enel", "cliente": "enel", "simples": false}""");

        var resultado1 = await _service.IniciarAsync(123, "post para enel");
        resultado1.Tipo.Should().Be(TipoResultadoPreFlight.PrecisaEsclarecimento);

        var resultado2 = await _service.ResumirAsync(123, "sim, enel");
        resultado2.Tipo.Should().Be(TipoResultadoPreFlight.Bloqueado);
        resultado2.MensagemBloqueio.Should().Contain("enel");
    }

    [Fact]
    public async Task ResumirAsync_WithRegisteredClientAfterClarification_ShouldReturnConcluido()
    {
        _refsMock.Setup(r => r.ListarClientes()).Returns(new List<string> { "acme" });
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post", "cliente": "desconhecido", "simples": false}""")
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Acme", "cliente": "acme", "simples": false}""")
            .ReturnsAsync("""{"briefing": "Post para Acme", "assets_reservados": []}""");

        var resultado1 = await _service.IniciarAsync(123, "post");
        resultado1.Tipo.Should().Be(TipoResultadoPreFlight.PrecisaEsclarecimento);

        var resultado2 = await _service.ResumirAsync(123, "na verdade e acme");
        resultado2.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado2.Cliente.Should().Be("acme");
    }

    [Fact]
    public async Task IniciarAsync_WithEmptyMontadorResponse_ShouldRetryWithDoubledMaxTokens()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post para Acme", "simples": false}""")
            .ReturnsAsync("")
            .ReturnsAsync("""{"briefing": "Briefing via retry com tokens dobrados", "assets_reservados": ["asset_1"]}""");

        var resultado = await _service.IniciarAsync(123, "post para acme");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Briefing.Should().Be("Briefing via retry com tokens dobrados");
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "preflight_montador_retry", It.IsAny<double>(), 4000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IniciarAsync_Pipeline_NaoDeveReservarAssets_MesmoQuandoMontadorInclui()
    {
        _chatMock.SetupSequence(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post Instagram Acme", "cliente": "acme", "simples": false}""")
            .ReturnsAsync("""{"briefing": "Post de Instagram para Acme com identidade visual da marca", "assets_reservados": ["asset_1", "asset_2"], "imagens_necessarias": ["imagem principal do post"]}""");

        var resultado = await _service.IniciarAsync(123, "crie um post de Instagram para a Acme");

        resultado.Tipo.Should().Be(TipoResultadoPreFlight.Concluido);
        resultado.Briefing.Should().Contain("Instagram");
        resultado.Briefing.Should().Contain("Acme");
    }

    [Fact]
    public async Task IniciarAsync_PromptDoMontador_NaoDeveListarCatalogoDeAssets()
    {
        string promptCapturado = "";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_montador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => promptCapturado = instrucoes)
            .ReturnsAsync("""{"briefing": "Post de Instagram para Acme"}""");
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "preflight_refinador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"precisa_esclarecimento": false, "pedido_refinado": "Post Instagram Acme", "cliente": "acme", "simples": false}""");

        await _service.IniciarAsync(123, "crie um post de Instagram para a Acme");

        promptCapturado.Should().NotContain("Catalogo de assets visuais disponiveis");
        promptCapturado.Should().NotContain("Catalogo de assets");
    }

    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
