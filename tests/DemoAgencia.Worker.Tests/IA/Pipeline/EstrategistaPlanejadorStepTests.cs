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

public class EstrategistaPlanejadorStepTests
{
    private readonly Mock<ILogger<EstrategistaPlanejadorStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly EstrategistaPlanejadorStep _step;

    public EstrategistaPlanejadorStepTests()
    {
        _loggerMock = new Mock<ILogger<EstrategistaPlanejadorStep>>();

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

        _step = new EstrategistaPlanejadorStep(
            _loggerMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object);
    }

    private static AgenteDefinicao CriarEstrategista() => new()
    {
        Nome = "Estrategista",
        ModeloAlvo = "llama",
        Persona = "persona",
        Papel = "estrategista"
    };

    private static AgenteDefinicao CriarRedator() => new()
    {
        Nome = "Redator",
        ModeloAlvo = "claude",
        Persona = "persona",
        Papel = "producao"
    };

    private static PipelineContext CriarContext() => new()
    {
        ChatId = 123,
        Mensagem = "crie um post",
        Rota = "pipeline",
        Briefing = "Criar post para Instagram",
        ListaAgentesProducao = "Redator: Escreve posts",
        AgentesProducao = new List<AgenteDefinicao> { CriarRedator() }.AsReadOnly(),
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_WithValidPlan_ShouldSetAgenteAndInstrucoes()
    {
        var estrategista = CriarEstrategista();
        var redator = CriarRedator();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_planejador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"agente\": \"Redator\", \"instrucoes\": \"Escreva um post sobre marketing\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.AgenteProducao.Should().NotBeNull();
        context.AgenteProducao!.Nome.Should().Be("Redator");
        context.InstrucoesOriginais.Should().Be("Escreva um post sobre marketing");
        context.InstrucoesProducao.Should().Be("Escreva um post sobre marketing");
        context.Resultado.EtapasExecutadas.Should().Contain("estrategista_planejador");
    }

    [Fact]
    public async Task ExecutarAsync_EstrategistaNotFound_ShouldReturnError()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns((AgenteDefinicao?)null);

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Contain("estrategista");
    }

    [Fact]
    public async Task ExecutarAsync_AgenteNotFound_ShouldFallbackToFirstProducao()
    {
        var estrategista = CriarEstrategista();
        var redator = CriarRedator();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("AgenteInexistente")).Returns((AgenteDefinicao?)null);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_planejador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"agente\": \"AgenteInexistente\", \"instrucoes\": \"Instrucoes\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.AgenteProducao.Should().NotBeNull();
        context.AgenteProducao!.Nome.Should().Be("Redator");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidJson_ShouldFallbackToBriefing()
    {
        var estrategista = CriarEstrategista();
        var redator = CriarRedator();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_planejador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta sem JSON valido");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.AgenteProducao.Should().NotBeNull();
        context.AgenteProducao!.Nome.Should().Be("Redator");
        context.InstrucoesOriginais.Should().Be("Criar post para Instagram");
        context.InstrucoesProducao.Should().Be("Criar post para Instagram");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_planejador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta sem JSON");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            Briefing = "Briefing",
            ListaAgentesProducao = "",
            AgentesProducao = new List<AgenteDefinicao>().AsReadOnly(),
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("📋 Planejando execucao...");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldPassBriefingAndAgentesToEstrategista()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_planejador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("Resposta sem JSON");

        var context = CriarContext();
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("Criar post para Instagram");
        capturedInstrucoes.Should().Contain("Redator: Escreve posts");
    }
}
