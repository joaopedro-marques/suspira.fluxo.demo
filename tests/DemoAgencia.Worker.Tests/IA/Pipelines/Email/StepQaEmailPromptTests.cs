using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepQaEmailPromptTests
{
    [Fact]
    public void MontarPromptQa_WithEstrategia_ShouldIncludeCriteria()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "link"),
            Html = "<html></html>",
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia
                {
                    Fase = "pos-compra",
                    Temas = new List<string> { "Boas vindas", "Financeiro" }
                },
                MapaEmocional = new List<EtapaEmocional>
                {
                    new() { Etapa = "CONTRATO", Sentimentos = new List<string> { "ANIMACAO" } }
                },
                SubJornada = "Pos Financiamento"
            }
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Estrategia de jornada");
        prompt.Should().Contain("pos-compra");
        prompt.Should().Contain("Boas vindas");
        prompt.Should().Contain("ANIMACAO");
        prompt.Should().Contain("Pos Financiamento");
        prompt.Should().Contain("alinhadas com a fase");
    }

    [Fact]
    public void MontarPromptQa_WithBanner_ShouldIncludeBannerInfo()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "link"),
            Html = "<html></html>",
            BannerSrc = "assets/agendar_vistoria.png"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Banner de referencia");
        prompt.Should().Contain("assets/agendar_vistoria.png");
    }

    [Fact]
    public void MontarPromptQa_WithoutBanner_ShouldIndicateAbsent()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "link"),
            Html = "<html></html>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Banner de referencia");
        prompt.Should().Contain("nenhum banner afim");
    }

    [Fact]
    public void MontarPromptQa_WithoutEstrategia_ShouldNotIncludeCriteria()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "acme",
            MensagemOriginal = "email",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), null, null),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "link"),
            Html = "<html></html>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().NotContain("Estrategia de jornada");
    }

    [Theory]
    [InlineData("Hero", "hero")]
    [InlineData("  copy  ", "copy")]
    [InlineData("imagem", "hero")]
    [InlineData("imagem_hero", "hero")]
    [InlineData("diagramacao", "diagramacao")]
    [InlineData("Diagramacao", "diagramacao")]
    [InlineData("copy", "copy")]
    public void ParseQaResult_ShouldNormalizeStepAlvo(string input, string expected)
    {
        var json = $"{{\"aprovado\": false, \"feedback\": \"fix\", \"step_alvo\": \"{input}\"}}";

        var (aprovado, feedback, stepAlvo) = StepQaEmail.ParseQaResult(json);

        aprovado.Should().BeFalse();
        stepAlvo.Should().Be(expected);
    }

    [Fact]
    public void ParseQaResult_WithNullStepAlvo_ShouldDefaultToCopy()
    {
        var json = "{\"aprovado\": false, \"feedback\": \"fix\"}";

        var (_, _, stepAlvo) = StepQaEmail.ParseQaResult(json);

        stepAlvo.Should().Be("copy");
    }

    [Fact]
    public void ParseQaResult_WithUnknownStepAlvo_ShouldDefaultToCopy()
    {
        var json = "{\"aprovado\": false, \"feedback\": \"fix\", \"step_alvo\": \"unknown_step\"}";

        var (_, _, stepAlvo) = StepQaEmail.ParseQaResult(json);

        stepAlvo.Should().Be("copy");
    }

    [Fact]
    public void MontarPromptQa_WithPlaceholders_ShouldIncludeEspSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "Confirmar presenca", "%%LINKASSEMBLEIA%%"),
            Html = "<p>Assembleia em %%DATA%% as %%HORARIO%% no %%LOCAL%%</p><a href=\"%%LINKASSEMBLEIA%%\">CTA</a>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Placeholders do ESP");
        prompt.Should().Contain("%%LINKASSEMBLEIA%%");
        prompt.Should().Contain("%%DATA%%");
        prompt.Should().Contain("%%HORARIO%%");
        prompt.Should().Contain("%%LOCAL%%");
        prompt.Should().Contain("nao sao defeitos");
    }

    [Fact]
    public void MontarPromptQa_WithoutPlaceholders_ShouldNotIncludeEspSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "https://link.com"),
            Html = "<p>Sem placeholders aqui</p>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().NotContain("Placeholders do ESP");
    }

    [Fact]
    public void ExtrairPlaceholders_ShouldReturnUniqueSorted()
    {
        var html = "<p>%%NOME%% %%DATA%% %%nome%% %%LINK%%</p>";

        var placeholders = StepQaEmail.ExtrairPlaceholders(html);

        placeholders.Should().BeEquivalentTo(new[] { "DATA", "LINK", "NOME" });
    }

    [Fact]
    public void ExtrairPlaceholders_EmptyHtml_ShouldReturnEmpty()
    {
        var placeholders = StepQaEmail.ExtrairPlaceholders("");

        placeholders.Should().BeEmpty();
    }

    [Fact]
    public void MontarPromptQa_WithBriefLink_ShouldIncludeCanonicalTokens()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "confirmar presenca na assembleia",
            Brief = new Brief("email", null, null, null, null, "%%LINKASSEMBLEIA%%", new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "Confirmar", "%%CONFIRM_LINK%%"),
            Html = "<a href=\"%%CONFIRM_LINK%%\">CTA</a>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Placeholders do briefing");
        prompt.Should().Contain("%%LINKASSEMBLEIA%%");
        prompt.Should().Contain("EXATAMENTE com o mesmo nome");
        prompt.Should().Contain("defeito critico");
    }

    [Fact]
    public void MontarPromptQa_WithoutBriefTokens_ShouldNotIncludeCanonicalSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "https://link.com"),
            Html = "<p>Sem tokens no briefing</p>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().NotContain("Placeholders do briefing");
    }

    [Theory]
    [InlineData("<p>Ola, %%NOME%%!</p>", 1)]
    [InlineData("<p>Ola, %%NOME%%!</p><p>Ola, %%NOME%%!</p>", 2)]
    [InlineData("<p>Sem saudacao</p>", 0)]
    [InlineData("<p>Olá, %%NOME%%!</p>", 1)]
    public void ContarSaudacoes_ShouldReturnCorrectCount(string html, int expected)
    {
        StepQaEmail.ContarSaudacoes(html).Should().Be(expected);
    }

    [Theory]
    [InlineData("<a href=\"x\">CTA</a>", "CTA", 1)]
    [InlineData("<a href=\"x\">CTA</a><a href=\"y\">CTA</a>", "CTA", 2)]
    [InlineData("<a href=\"x\">CTA</a><a href=\"y\">Outro</a>", "CTA", 1)]
    [InlineData("<a href=\"x\">Confirmar presença</a>", "Confirmar presença", 1)]
    [InlineData("<a href=\"x\">Confirmar &amp; participar</a>", "Confirmar & participar", 1)]
    public void ContarCtas_ShouldReturnCorrectCount(string html, string ctaTexto, int expected)
    {
        StepQaEmail.ContarCtas(html, ctaTexto).Should().Be(expected);
    }

    [Fact]
    public void MontarPromptQa_WithOneSaudacao_ShouldIncludeFactualSection()
    {
        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "CTA", "link"),
            Html = "<p>Ola, %%NOME%%!</p><a href=\"x\">CTA</a>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().Contain("Verificacao deterministica");
        prompt.Should().Contain("1 ocorrencia(s) no HTML");
        prompt.Should().Contain("esperado (template)");
    }
}
