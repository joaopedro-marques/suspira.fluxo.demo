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

public class QualidadeStepTests
{
    private readonly Mock<ILogger<QualidadeStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly QualidadeStep _step;

    public QualidadeStepTests()
    {
        _loggerMock = new Mock<ILogger<QualidadeStep>>();

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

        _step = new QualidadeStep(
            _loggerMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object);
    }

    private static AgenteDefinicao CriarQualidade() => new()
    {
        Nome = "Qualidade",
        ModeloAlvo = "gemini-flash",
        Persona = "persona",
        Papel = "qualidade"
    };

    private static PipelineContext CriarContext() => new()
    {
        ChatId = 123,
        Mensagem = "crie um post",
        Rota = "pipeline",
        InstrucoesOriginais = "Escreva um post sobre marketing",
        OutputProducao = "Post criado com sucesso",
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_Aprovado_ShouldSetVereditoAndContinue()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"veredito\": \"aprovado\", \"feedback\": \"Excelente trabalho\"}");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeFalse();
        context.VereditoQualidade.Should().Be("aprovado");
        context.FeedbackQualidade.Should().Be("Excelente trabalho");
        context.Resultado.EtapasExecutadas.Should().Contain("qualidade");
    }

    [Fact]
    public async Task ExecutarAsync_Reprovado_ShouldSetDeveRefazer()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"veredito\": \"reprovado\", \"feedback\": \"Melhore o texto\"}");

        var context = CriarContext();
        context.Refacoes = 0;
        context.MaxRefacoes = 2;
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeTrue();
        context.VereditoQualidade.Should().Be("reprovado");
        context.FeedbackQualidade.Should().Be("Melhore o texto");
    }

    [Fact]
    public async Task ExecutarAsync_QualidadeNotFound_ShouldReturnError()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns((AgenteDefinicao?)null);

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Contain("qualidade");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidJson_ShouldAssumeAprovado()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta sem JSON valido");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        result.DeveRefazer.Should().BeFalse();
        context.VereditoQualidade.Should().Be("aprovado");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldIncludeFeedbackAnteriorWhenRefacoes()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("{\"veredito\": \"aprovado\"}");

        var context = CriarContext();
        context.Refacoes = 1;
        context.FeedbackAnterior = "Feedback da iteracao anterior";
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("Feedback da iteracao anterior");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"veredito\": \"aprovado\"}");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            InstrucoesOriginais = "instrucoes",
            OutputProducao = "output",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("🔍 Revisando qualidade...");
    }

    [Fact]
    public async Task ExecutarAsync_WithCriteriosQa_ShouldIncludeInPrompt()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("{\"veredito\": \"aprovado\"}");

        var context = CriarContext();
        context.CriteriosQa = new List<string> { "Usar cor #FF0000", "Incluir CTA" }.AsReadOnly();
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("Criterios objetivos");
        capturedInstrucoes.Should().Contain("Usar cor #FF0000");
        capturedInstrucoes.Should().Contain("Incluir CTA");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutCriteriosQa_ShouldNotIncludeCriteriaBlock()
    {
        var qualidade = CriarQualidade();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "qualidade", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("{\"veredito\": \"aprovado\"}");

        var context = CriarContext();
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().NotContain("Criterios objetivos");
    }
}
