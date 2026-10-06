using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepCopyEmailTests
{
    private readonly Mock<IServicoChat> _chatMock = new();
    private readonly AgenteDefinicao _agente = new("redator", "test/model", 0.8, 8000, "persona");

    private StepCopyEmail CriarStep() => new(_agente, _chatMock.Object, Mock.Of<ILogger<StepCopyEmail>>());

    private static string JsonResposta(string ctaLink = "https://link.com") =>
        $"{{\"assunto\":\"Teste\",\"preheader\":\"pre\",\"titulo\":\"T\",\"saudacao\":\"ola\",\"corpo\":\"<p>corpo</p>\",\"cta_texto\":\"CTA\",\"cta_link\":\"{ctaLink}\",\"rodape\":\"rod\"}}";

    [Fact]
    public async Task ExecutarAsync_WhenBriefLinkPresent_ShouldOverrideCtaLinkVerbatim()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonResposta("%%CONFIRM_LINK%%"));

        var step = CriarStep();
        var ctx = new PipelineContext
        {
            ChatId = 1, Cliente = "mrv", MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, "%%LINKASSEMBLEIA%%", new(), new())
        };

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Copy!.CtaLink.Should().Be("%%LINKASSEMBLEIA%%");
    }

    [Fact]
    public async Task ExecutarAsync_WhenBriefLinkPresentWithUrl_ShouldOverrideCtaLink()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonResposta("https://wrong.com"));

        var step = CriarStep();
        var ctx = new PipelineContext
        {
            ChatId = 1, Cliente = "mrv", MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, "https://correct.com", new(), new())
        };

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Copy!.CtaLink.Should().Be("https://correct.com");
    }

    [Fact]
    public async Task ExecutarAsync_WhenBriefLinkAbsent_ShouldKeepRedatorCtaLink()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonResposta("https://redator.com"));

        var step = CriarStep();
        var ctx = new PipelineContext
        {
            ChatId = 1, Cliente = "mrv", MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new(), new())
        };

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Copy!.CtaLink.Should().Be("https://redator.com");
    }
    [Fact]
    public void MontarPrompt_WithEstrategia_ShouldIncludeStrategySection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", "Pos Financiamento"),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia
                {
                    Fase = "pos-compra",
                    CorPrincipal = "Roxo",
                    DescricaoCor = "Transformacao e sonho",
                    Temas = new List<string> { "Boas vindas", "Financeiro" }
                },
                SubJornada = "Pos Financiamento"
            }
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().Contain("Estrategia de jornada");
        prompt.Should().Contain("pos-compra");
        prompt.Should().Contain("Roxo");
        prompt.Should().Contain("Boas vindas");
        prompt.Should().Contain("Pos Financiamento");
    }

    [Fact]
    public void MontarPrompt_WithSatisfacoes_ShouldIncludeGuidance()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia { Fase = "pos-compra" },
                Satisfacoes = new List<CategoriaSatisfacao>
                {
                    new() { Nome = "Atendimento", Itens = new List<string> { "Elogio", "Geral" } }
                },
                Insatisfacoes = new List<CategoriaSatisfacao>
                {
                    new() { Nome = "Entrega", Itens = new List<string> { "Demora", "Atraso" } }
                }
            }
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().Contain("satisfacoes");
        prompt.Should().Contain("Elogio");
        prompt.Should().Contain("Demora");
    }

    [Fact]
    public void MontarPrompt_WithMapaEmocional_ShouldIncludeSentimentos()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia { Fase = "pos-compra" },
                MapaEmocional = new List<EtapaEmocional>
                {
                    new() { Etapa = "CONTRATO", Sentimentos = new List<string> { "ANIMACAO", "FELICIDADE" } }
                }
            }
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().Contain("Sentimentos");
        prompt.Should().Contain("ANIMACAO");
    }

    [Fact]
    public void MontarPrompt_WithoutEstrategia_ShouldNotIncludeStrategySection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "acme",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), null, null),
            Estrategia = null
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().NotContain("Estrategia de jornada");
    }

    [Fact]
    public void MontarPrompt_WithQaFeedback_ShouldIncludeRevisionSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), null, null),
            Refacoes = 2,
            QaFeedback = "Hero image nao tem relacao com o conteudo. Corrigir para algo mais relevante."
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().Contain("REVISAO NECESSARIA");
        prompt.Should().Contain("refacao 2");
        prompt.Should().Contain("Hero image nao tem relacao");
    }

    [Fact]
    public void MontarPrompt_WithoutQaFeedback_ShouldNotIncludeRevisionSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), null, null),
            Refacoes = 0
        };

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().NotContain("REVISAO NECESSARIA");
    }
}
