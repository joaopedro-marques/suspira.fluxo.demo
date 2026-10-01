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

public class AprovadorStepTests
{
    private readonly Mock<ILogger<AprovadorStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly AprovadorStep _step;

    public AprovadorStepTests()
    {
        _loggerMock = new Mock<ILogger<AprovadorStep>>();

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

        _step = new AprovadorStep(
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

    private static PipelineContext CriarContext() => new()
    {
        ChatId = 123,
        Mensagem = "crie um post",
        Rota = "pipeline",
        Briefing = "Criar post para Instagram",
        OutputProducao = "Post criado com sucesso",
        VereditoQualidade = "aprovado",
        FeedbackQualidade = "OK",
        Refacoes = 0,
        MaxRefacoes = 2,
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_Aprovado_ShouldSetAprovadoFinal()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_aprovador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true, \"observacoes\": \"Excelente trabalho\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeFalse();
        context.AprovadoEstrategista.Should().BeTrue();
        context.ObservacoesEstrategista.Should().Be("Excelente trabalho");
        context.Resultado.EtapasExecutadas.Should().Contain("estrategista_aprovador");
    }

    [Fact]
    public async Task ExecutarAsync_Reprovado_ComRefacoes_ShouldSetDeveRefazer()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_aprovador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": false, \"observacoes\": \"Melhore o texto\"}");

        var context = CriarContext();
        context.Refacoes = 0;
        context.MaxRefacoes = 2;
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeTrue();
        context.AprovadoEstrategista.Should().BeFalse();
        context.ObservacoesEstrategista.Should().Be("Melhore o texto");
    }

    [Fact]
    public async Task ExecutarAsync_Reprovado_SemRefacoes_ShouldNotRefazer()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_aprovador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": false, \"observacoes\": \"Ruim\"}");

        var context = CriarContext();
        context.Refacoes = 2;
        context.MaxRefacoes = 2;
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeFalse();
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
    public async Task ExecutarAsync_InvalidJson_ShouldAssumeAprovado()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_aprovador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta sem JSON valido");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeFalse();
        context.AprovadoEstrategista.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var estrategista = CriarEstrategista();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "estrategista_aprovador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true}");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            Briefing = "Briefing",
            OutputProducao = "output",
            VereditoQualidade = "aprovado",
            FeedbackQualidade = "feedback",
            Refacoes = 0,
            MaxRefacoes = 2,
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("✅ Aprovando...");
    }
}
