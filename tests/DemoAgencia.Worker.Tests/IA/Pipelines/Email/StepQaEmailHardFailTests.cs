using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepQaEmailHardFailTests
{
    private readonly Mock<IServicoChat> _chatMock = new();
    private readonly AgenteDefinicao _agente = new("qa", "test/model", 0.3, 8000, "persona");

    private StepQaEmail CriarStep() => new(_agente, _chatMock.Object);

    private static PipelineContext CriarContexto(string html, string ctaTexto = "CTA") => new()
    {
        ChatId = 1,
        Cliente = "mrv",
        MensagemOriginal = "email",
        Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
        Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", ctaTexto, "link"),
        Html = html
    };

    [Fact]
    public async Task ExecutarAsync_WithTwoSaudacoes_ShouldHardFailWithoutCallingLLM()
    {
        var step = CriarStep();
        var ctx = CriarContexto("<p>Ola, %%NOME%%!</p><p>Ola, %%NOME%%!</p>");

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.QaAprovado.Should().BeFalse();
        result.QaFeedback.Should().Contain("Duplicacao de saudacao");
        result.QaStepAlvo.Should().Be("diagramacao");
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_WithTwoCtas_ShouldHardFailWithoutCallingLLM()
    {
        var step = CriarStep();
        var ctx = CriarContexto("<a href=\"x\">CTA</a><a href=\"y\">CTA</a>");

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.QaAprovado.Should().BeFalse();
        result.QaFeedback.Should().Contain("Duplicacao de CTA");
        result.QaStepAlvo.Should().Be("diagramacao");
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_WithOneOfEach_ShouldCallLLM()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true, \"feedback\": \"ok\"}");

        var step = CriarStep();
        var ctx = CriarContexto("<p>Ola, %%NOME%%!</p><a href=\"x\">CTA</a>");

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.QaAprovado.Should().BeTrue();
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
