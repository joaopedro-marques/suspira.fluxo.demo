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

public class DiretaStepTests
{
    private readonly Mock<ILogger<DiretaStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly HistoricoChat _historico;
    private readonly DiretaStep _step;

    public DiretaStepTests()
    {
        _loggerMock = new Mock<ILogger<DiretaStep>>();
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

        _step = new DiretaStep(
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

    private static PipelineContext CriarContext(string respostaDireta = "Resposta direta") => new()
    {
        ChatId = 123,
        Mensagem = "o que e marketing?",
        Rota = "direta",
        RespostaDireta = respostaDireta,
        RespostaOrquestrador = "{\"acao\": \"direta\", \"resposta\": \"Resposta direta\"}",
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
            .ReturnsAsync("Resposta formatada para Telegram");

        var context = CriarContext();
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Be("Resposta formatada para Telegram");
        context.Resultado.EtapasExecutadas.Should().Contain("formatador");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutFormatador_ShouldReturnRawResponse()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns((AgenteDefinicao?)null);

        var context = CriarContext("Resposta crua");
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Be("Resposta crua");
    }

    [Fact]
    public async Task ExecutarAsync_WithNullRespostaDireta_ShouldFallbackToRespostaOrquestrador()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns((AgenteDefinicao?)null);

        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "direta",
            RespostaDireta = null,
            RespostaOrquestrador = "Resposta do orquestrador como fallback",
            CancellationToken = CancellationToken.None
        };

        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeFalse();
        context.Resultado.RespostaFinal.Should().Be("Resposta do orquestrador como fallback");
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
        historico[0].Content.Should().Be("o que e marketing?");
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
            Rota = "direta",
            RespostaDireta = "Resposta",
            RespostaOrquestrador = "raw",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("📤 Formatando resposta...");
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

        var context = CriarContext("Resposta para formatar");
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Contain("o que e marketing?");
        capturedInstrucoes.Should().Contain("Resposta para formatar");
    }
}
