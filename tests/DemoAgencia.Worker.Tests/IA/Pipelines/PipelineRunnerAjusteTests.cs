using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Router;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines;

public class PipelineRunnerAjusteTests
{
    private readonly PipelineRunner _runner = new(Mock.Of<ILogger<PipelineRunner>>());

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

        public FakeStep(string nome) => Nome = nome;

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
        public Queue<bool> Aprovacoes { get; } = new();
        public string? StepAlvo { get; set; }
        public string? Feedback { get; set; }

        public Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
        {
            ExecCount++;
            context.QaAprovado = Aprovacoes.Count > 0 ? Aprovacoes.Dequeue() : true;
            context.QaFeedback = Feedback;
            context.QaStepAlvo = StepAlvo;
            return Task.FromResult(context);
        }
    }

    private static List<IPipelineStep> CriarSteps(
        FakeStep estrategia,
        FakeStep copy,
        FakeStep diagramacao,
        FakeStep hero,
        FakeStep template,
        IPipelineStep qa)
    {
        return new List<IPipelineStep> { estrategia, copy, diagramacao, hero, template, qa };
    }

    [Fact]
    public async Task ExecutarAjusteAsync_ShouldStartAtTargetStep()
    {
        var estrategia = new FakeStep("estrategia");
        var copy = new FakeStep("copy");
        var diagramacao = new FakeStep("diagramacao");
        var hero = new FakeStep("hero");
        var template = new FakeStep("template");
        var qa = new FakeQaStep();

        await _runner.ExecutarAjusteAsync(
            CriarContexto(),
            CriarSteps(estrategia, copy, diagramacao, hero, template, qa),
            "diagramacao",
            maxRefacoesQa: 4,
            CancellationToken.None);

        estrategia.ExecCount.Should().Be(0);
        copy.ExecCount.Should().Be(0);
        diagramacao.ExecCount.Should().Be(1);
        hero.ExecCount.Should().Be(1);
        template.ExecCount.Should().Be(1);
        qa.ExecCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutarAjusteAsync_CopyTarget_ShouldPreserveHero()
    {
        var estrategia = new FakeStep("estrategia");
        var copy = new FakeStep("copy");
        var diagramacao = new FakeStep("diagramacao");
        var hero = new FakeStep("hero");
        var template = new FakeStep("template");
        var qa = new FakeStep("qa");

        var contexto = CriarContexto();
        contexto.HeroSrc = "imagens/gerada_1.png";
        contexto.Resultado.Imagens.Add(new ImagemGerada(new byte[] { 1 }, "Hero image 1"));
        var preservou = false;
        hero.OnExecute = ctx =>
        {
            preservou = ctx.PreservarHero;
            return ctx;
        };

        await _runner.ExecutarAjusteAsync(
            contexto,
            CriarSteps(estrategia, copy, diagramacao, hero, template, qa),
            "copy",
            maxRefacoesQa: 4,
            CancellationToken.None);

        preservou.Should().BeTrue();
        contexto.HeroSrc.Should().Be("imagens/gerada_1.png");
        contexto.Resultado.Imagens.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecutarAjusteAsync_HeroTarget_ShouldClearPreviousMedia()
    {
        var estrategia = new FakeStep("estrategia");
        var copy = new FakeStep("copy");
        var diagramacao = new FakeStep("diagramacao");
        var hero = new FakeStep("hero");
        var template = new FakeStep("template");
        var qa = new FakeStep("qa");

        var contexto = CriarContexto();
        contexto.HeroSrc = "imagens/gerada_1.png";
        contexto.BannerSrc = "assets/banner.png";
        contexto.Resultado.Imagens.Add(new ImagemGerada(new byte[] { 1 }, "Hero image 1"));
        contexto.Resultado.AssetsAnexados.Add(new ImagemGerada(new byte[] { 2 }, "banner.png"));
        contexto.Resultado.AssetsAnexados.Add(new ImagemGerada(new byte[] { 3 }, "logo.png"));

        var preservou = true;
        hero.OnExecute = ctx =>
        {
            preservou = ctx.PreservarHero;
            ctx.HeroSrc = "imagens/gerada_1.png";
            return ctx;
        };

        await _runner.ExecutarAjusteAsync(
            contexto,
            CriarSteps(estrategia, copy, diagramacao, hero, template, qa),
            "hero",
            maxRefacoesQa: 4,
            CancellationToken.None);

        preservou.Should().BeFalse();
        contexto.Resultado.Imagens.Should().BeEmpty();
        contexto.Resultado.AssetsAnexados.Should().ContainSingle(a => a.Legenda == "logo.png");
        contexto.BannerSrc.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAjusteAsync_ShouldResetQaState()
    {
        var copy = new FakeStep("copy");
        var qa = new FakeQaStep();

        var contexto = CriarContexto();
        contexto.Refacoes = 3;
        contexto.QaFeedback = "feedback antigo";
        contexto.QaStepAlvo = "hero";
        contexto.QaAprovado = false;

        await _runner.ExecutarAjusteAsync(
            contexto,
            new List<IPipelineStep> { copy, qa },
            "copy",
            maxRefacoesQa: 4,
            CancellationToken.None);

        contexto.Refacoes.Should().Be(0);
        contexto.Resultado.QaAprovado.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutarAjusteAsync_UnknownTarget_ShouldFallbackToCopy()
    {
        var copy = new FakeStep("copy");
        var qa = new FakeQaStep();

        await _runner.ExecutarAjusteAsync(
            CriarContexto(),
            new List<IPipelineStep> { copy, qa },
            "inexistente",
            maxRefacoesQa: 4,
            CancellationToken.None);

        copy.ExecCount.Should().Be(1);
        qa.ExecCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutarAjusteAsync_QaReprovaDepoisAprova_ShouldRerunTarget()
    {
        var copy = new FakeStep("copy");
        var hero = new FakeStep("hero");
        var template = new FakeStep("template");
        var qa = new FakeQaStep { StepAlvo = "diagramacao", Feedback = "corrija as cores" };
        qa.Aprovacoes.Enqueue(false);
        qa.Aprovacoes.Enqueue(true);

        await _runner.ExecutarAjusteAsync(
            CriarContexto(),
            new List<IPipelineStep> { copy, hero, template, qa },
            "copy",
            maxRefacoesQa: 4,
            CancellationToken.None);

        copy.ExecCount.Should().Be(2);
        qa.ExecCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecutarAjusteAsync_TargetAfterQaFallback_ShouldNotThrow()
    {
        var copy = new FakeStep("copy");
        var hero = new FakeStep("hero");
        var template = new FakeStep("template");
        var qa = new FakeQaStep { StepAlvo = "copy", Feedback = "problema na copy" };
        qa.Aprovacoes.Enqueue(false);
        qa.Aprovacoes.Enqueue(true);

        // Ajuste comecando no template: QA pede copy, que fica antes do inicio da rodada.
        var act = async () => await _runner.ExecutarAjusteAsync(
            CriarContexto(),
            new List<IPipelineStep> { copy, hero, template, qa },
            "template",
            maxRefacoesQa: 4,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecutarAjusteAsync_HeroRetry_ShouldDisablePreservacao()
    {
        var copy = new FakeStep("copy");
        var hero = new FakeStep("hero");
        var qa = new FakeQaStep { StepAlvo = "hero", Feedback = "imagem ruim" };
        qa.Aprovacoes.Enqueue(false);
        qa.Aprovacoes.Enqueue(true);

        var preservouNoRetry = true;
        var chamadas = 0;
        hero.OnExecute = ctx =>
        {
            chamadas++;
            if (chamadas > 1)
                preservouNoRetry = ctx.PreservarHero;
            return ctx;
        };

        await _runner.ExecutarAjusteAsync(
            CriarContexto(),
            new List<IPipelineStep> { copy, hero, qa },
            "copy",
            maxRefacoesQa: 4,
            CancellationToken.None);

        preservouNoRetry.Should().BeFalse();
        hero.ExecCount.Should().Be(2);
    }
}
