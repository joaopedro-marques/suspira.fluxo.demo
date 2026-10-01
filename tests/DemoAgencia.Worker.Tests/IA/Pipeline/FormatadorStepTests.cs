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

public class FormatadorStepTests
{
    private readonly Mock<ILogger<FormatadorStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly HistoricoChat _historico;
    private readonly FormatadorStep _step;

    public FormatadorStepTests()
    {
        _loggerMock = new Mock<ILogger<FormatadorStep>>();
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

        _step = new FormatadorStep(
            _loggerMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _historico);
    }

    private static AgenteDefinicao CriarFormatador() => new()
    {
        Nome = "Formatador",
        ModeloAlvo = "gemini-flash",
        Persona = "persona",
        Papel = "formatacao"
    };

    private static PipelineContext CriarContext() => new()
    {
        ChatId = 123,
        Mensagem = "crie um post",
        Rota = "pipeline",
        OutputProducao = "Post criado com sucesso sobre marketing",
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_WithFormatador_ShouldReturnFormattedResponse()
    {
        var formatador = CriarFormatador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "formatador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Post formatado para Telegram com emojis");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Be("Post formatado para Telegram com emojis");
        context.Resultado.EtapasExecutadas.Should().Contain("formatador");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutFormatador_ShouldReturnRawOutput()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns((AgenteDefinicao?)null);

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Be("Post criado com sucesso sobre marketing");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldSaveToHistorico()
    {
        var formatador = CriarFormatador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "formatador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta formatada");

        var context = CriarContext();
        await _step.ExecutarAsync(context);

        var historico = _historico.ObterHistorico(123);
        historico.Should().HaveCount(2);
        historico[0].Role.Should().Be("user");
        historico[0].Content.Should().Be("crie um post");
        historico[1].Role.Should().Be("assistant");
        historico[1].Content.Should().Be("Resposta formatada");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var formatador = CriarFormatador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "formatador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta formatada");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            OutputProducao = "output",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("📤 Formatando resposta final...");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldPassCorrectFormatInstructions()
    {
        var formatador = CriarFormatador();
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "formatador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("Resposta formatada");

        var context = CriarContext();
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("crie um post");
        capturedInstrucoes.Should().Contain("Post criado com sucesso sobre marketing");
    }

    [Fact]
    public async Task ExecutarAsync_WithNullOutputProducao_ShouldReturnEmpty()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns((AgenteDefinicao?)null);

        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            OutputProducao = null,
            CancellationToken = CancellationToken.None
        };

        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().BeEmpty();
    }
}
