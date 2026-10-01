using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipeline;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipeline;

public class OrquestradorStepTests
{
    private readonly Mock<ILogger<OrquestradorStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly HistoricoChat _historico;
    private readonly OrquestradorStep _step;

    public OrquestradorStepTests()
    {
        _loggerMock = new Mock<ILogger<OrquestradorStep>>();
        _historico = new HistoricoChat();

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

        _agenteLoaderMock = new Mock<AgenteLoader>(
            Mock.Of<ILogger<AgenteLoader>>(),
            Mock.Of<IConfiguration>(),
            (string?)null);

        _step = new OrquestradorStep(
            _loggerMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _historico);
    }

    private static AgenteDefinicao CriarOrquestrador() => new()
    {
        Nome = "Orquestrador",
        ModeloAlvo = "gemini-flash",
        Persona = "persona",
        Papel = "orquestrador"
    };

    private static PipelineContext CriarContext() => new()
    {
        ChatId = 123,
        Mensagem = "mensagem de teste",
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_ForAcaoPipeline_ShouldSetRotaAndBriefing()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"pipeline\", \"briefing\": \"Criar post para Instagram\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.Rota.Should().Be("pipeline");
        context.Briefing.Should().Be("Criar post para Instagram");
        context.RespostaOrquestrador.Should().Be("{\"acao\": \"pipeline\", \"briefing\": \"Criar post para Instagram\"}");
        context.Resultado.Rota.Should().Be("pipeline");
        context.Resultado.EtapasExecutadas.Should().Contain("orquestrador");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoForaContexto_ShouldSetRota()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.Rota.Should().Be("fora_contexto");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoDireta_ShouldSetRotaAndResposta()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"direta\", \"resposta\": \"Resposta direta\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.Rota.Should().Be("direta");
        context.RespostaDireta.Should().Be("Resposta direta");
    }

    [Fact]
    public async Task ExecutarAsync_OrquestradorNotFound_ShouldSetErrorResult()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns((AgenteDefinicao?)null);

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Contain("orquestrador");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidJson_ShouldFallbackToDireta()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta sem JSON valido");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.Rota.Should().Be("direta");
        context.RespostaDireta.Should().Be("Resposta sem JSON valido");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("🧠 Analisando seu pedido...");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldBuildHistoricoContext()
    {
        var orquestrador = CriarOrquestrador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        _historico.AdicionarMensagem(123, "user", "mensagem anterior");
        _historico.AdicionarMensagem(123, "assistant", "resposta anterior");

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var context = CriarContext();
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("mensagem anterior");
        capturedInstrucoes.Should().Contain("resposta anterior");
        capturedInstrucoes.Should().Contain("mensagem de teste");
    }
}
