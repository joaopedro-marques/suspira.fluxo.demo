using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Router;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines;

public class PipelineRunnerTests
{
    private readonly Mock<ILogger<PipelineRunner>> _loggerMock;
    private readonly PipelineRunner _runner;

    public PipelineRunnerTests()
    {
        _loggerMock = new Mock<ILogger<PipelineRunner>>();
        _runner = new PipelineRunner(_loggerMock.Object);
    }

    private static PipelineContext CriarContexto()
    {
        return new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>())
        };
    }

    private class FakeStep : IPipelineStep
    {
        public string Nome { get; }
        public int ExecCount { get; private set; }
        public Func<PipelineContext, PipelineContext>? OnExecute { get; set; }

        public FakeStep(string nome)
        {
            Nome = nome;
        }

        public Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
        {
            ExecCount++;
            if (OnExecute != null)
                context = OnExecute(context);
            return Task.FromResult(context);
        }
    }

    private class FakeQaStep : IPipelineStep
    {
        public string Nome => "qa";
        public int ExecCount { get; private set; }
        public List<bool> AprovadoSequence { get; } = new();
        public string? Feedback { get; set; }
        public string? StepAlvo { get; set; }

        public Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
        {
            var idx = ExecCount;
            ExecCount++;
            context.QaAprovado = idx < AprovadoSequence.Count ? AprovadoSequence[idx] : AprovadoSequence.LastOrDefault();
            context.QaFeedback = Feedback;
            context.QaStepAlvo = StepAlvo;
            return Task.FromResult(context);
        }
    }

    [Fact]
    public async Task ExecutarAsync_QaApproves_ShouldSetQaAprovadoTrue()
    {
        var copyStep = new FakeStep("copy");
        var qaStep = new FakeQaStep { AprovadoSequence = { true }, Feedback = "OK" };
        var steps = new List<IPipelineStep> { copyStep, qaStep };

        var context = CriarContexto();
        context.Resultado.RespostaFinal = "<html>email</html>";

        var result = await _runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, CancellationToken.None);

        result.QaAprovado.Should().BeTrue();
        result.RespostaFinal.Should().Be("<html>email</html>");
        copyStep.ExecCount.Should().Be(1);
        qaStep.ExecCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutarAsync_QaReprovesThenApproves_ShouldReexecuteTargetStep()
    {
        var copyStep = new FakeStep("copy");
        var heroStep = new FakeStep("hero");
        var qaStep = new FakeQaStep
        {
            AprovadoSequence = { false, true },
            Feedback = "Fix copy",
            StepAlvo = "copy"
        };
        var steps = new List<IPipelineStep> { copyStep, heroStep, qaStep };

        var context = CriarContexto();
        context.Resultado.RespostaFinal = "<html>email</html>";

        var result = await _runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, CancellationToken.None);

        result.QaAprovado.Should().BeTrue();
        copyStep.ExecCount.Should().Be(2);
        heroStep.ExecCount.Should().Be(2);
        qaStep.ExecCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecutarAsync_QaAlwaysReproves_ShouldSetQaAprovadoFalseAndPreserveHtml()
    {
        var copyStep = new FakeStep("copy");
        var qaStep = new FakeQaStep
        {
            AprovadoSequence = { false, false, false },
            Feedback = "QA feedback text",
            StepAlvo = "copy"
        };
        var steps = new List<IPipelineStep> { copyStep, qaStep };

        var context = CriarContexto();
        context.Resultado.RespostaFinal = "<html>last good html</html>";

        var result = await _runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, CancellationToken.None);

        result.QaAprovado.Should().BeFalse();
        result.QaFeedbackFinal.Should().Be("QA feedback text");
        result.RespostaFinal.Should().Be("<html>last good html</html>");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidStepAlvo_ShouldFallbackToCopy()
    {
        var copyStep = new FakeStep("copy");
        var qaStep = new FakeQaStep
        {
            AprovadoSequence = { false, true },
            Feedback = "Fix something",
            StepAlvo = "nonexistent_step"
        };
        var steps = new List<IPipelineStep> { copyStep, qaStep };

        var context = CriarContexto();
        context.Resultado.RespostaFinal = "<html>email</html>";

        var result = await _runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, CancellationToken.None);

        result.QaAprovado.Should().BeTrue();
        copyStep.ExecCount.Should().Be(2);
    }
}
