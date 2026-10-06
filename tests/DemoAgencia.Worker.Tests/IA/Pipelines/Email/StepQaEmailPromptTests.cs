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
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "ola", "corpo", "cta", "link", "rodape"),
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
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "ola", "corpo", "cta", "link", "rodape"),
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
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "ola", "corpo", "cta", "link", "rodape"),
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
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "ola", "corpo", "cta", "link", "rodape"),
            Html = "<html></html>"
        };

        var prompt = StepQaEmail.MontarPromptQa(contexto);

        prompt.Should().NotContain("Estrategia de jornada");
    }
}
