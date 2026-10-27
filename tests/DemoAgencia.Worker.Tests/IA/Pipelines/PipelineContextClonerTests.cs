using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Router;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines;

public class PipelineContextClonerTests
{
    private static PipelineContext CriarContexto()
    {
        var contexto = new PipelineContext
        {
            ChatId = 123,
            Cliente = "mrv",
            MensagemOriginal = "mensagem original",
            Brief = new Brief(
                "email", "engajar", "moradores", "oferta", "tom", "https://link",
                new List<string> { "sem emojis" },
                new List<ImagemBrief> { new("hero", "imagem") },
                "pos-compra", "sub", "tema",
                new List<string> { "https://extra" }),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra", SubJornada = "sub", TemaSelecionado = "tema" },
            LogoSrc = "assets/logo.png",
            Copy = new CopyEmailSlots("assunto", "pre", "titulo", "corpo", "cta", "https://cta"),
            HeroSrc = "imagens/gerada_1.png",
            BannerSrc = "assets/banner.png",
            TemplateId = "template",
            Html = "<html>entregue</html>",
            FixedAssetRefs = new[] { "logo.png" },
            Refacoes = 2,
            QaFeedback = "feedback",
            QaStepAlvo = "copy",
            QaAprovado = true
        };

        contexto.Resultado.RespostaFinal = "<html>entregue</html>";
        contexto.Resultado.Imagens.Add(new ImagemGerada(new byte[] { 1, 2 }, "Hero image 1"));
        contexto.Resultado.AssetsAnexados.Add(new ImagemGerada(new byte[] { 3 }, "logo.png"));
        contexto.Resultado.EtapasExecutadas.Add("copy");
        contexto.Resultado.QaAprovado = true;
        contexto.Resultado.QaFeedbackFinal = "ok";

        return contexto;
    }

    [Fact]
    public void Clonar_ShouldCopyScalarFields()
    {
        var original = CriarContexto();

        var clone = PipelineContextCloner.Clonar(original);

        clone.ChatId.Should().Be(original.ChatId);
        clone.Cliente.Should().Be("mrv");
        clone.MensagemOriginal.Should().Be("mensagem original");
        clone.Copy!.Assunto.Should().Be("assunto");
        clone.HeroSrc.Should().Be("imagens/gerada_1.png");
        clone.BannerSrc.Should().Be("assets/banner.png");
        clone.TemplateId.Should().Be("template");
        clone.Html.Should().Be("<html>entregue</html>");
        clone.Resultado.RespostaFinal.Should().Be("<html>entregue</html>");
    }

    [Fact]
    public void Clonar_ShouldResetQaState()
    {
        var clone = PipelineContextCloner.Clonar(CriarContexto());

        clone.QaFeedback.Should().BeNull();
        clone.QaStepAlvo.Should().BeNull();
        clone.QaAprovado.Should().BeFalse();
        clone.Refacoes.Should().Be(0);
    }

    [Fact]
    public void Clonar_ShouldDeepCopyLists()
    {
        var original = CriarContexto();

        var clone = PipelineContextCloner.Clonar(original);

        clone.Resultado.Imagens.Add(new ImagemGerada(new byte[] { 9 }, "nova"));
        clone.Resultado.AssetsAnexados.Add(new ImagemGerada(new byte[] { 9 }, "novo"));
        clone.Brief.Restricoes.Add("nova restricao");
        clone.Brief.Imagens.Add(new ImagemBrief("banner", "outra"));
        clone.Brief.LinksExtras!.Add("https://outro");
        clone.FixedAssetRefs = new List<string>(clone.FixedAssetRefs) { "extra.png" };
        clone.Resultado.EtapasExecutadas.Add("hero");

        original.Resultado.Imagens.Should().HaveCount(1);
        original.Resultado.AssetsAnexados.Should().HaveCount(1);
        original.Brief.Restricoes.Should().HaveCount(1);
        original.Brief.Imagens.Should().HaveCount(1);
        original.Brief.LinksExtras.Should().HaveCount(1);
        original.FixedAssetRefs.Should().HaveCount(1);
        original.Resultado.EtapasExecutadas.Should().HaveCount(1);
    }

    [Fact]
    public void Clonar_ShouldDeepCopyEstrategia()
    {
        var original = CriarContexto();

        var clone = PipelineContextCloner.Clonar(original);
        clone.Estrategia!.EtapaMapaEmocional = "alterada";
        clone.Estrategia!.FatorDecisao = "R";

        original.Estrategia!.EtapaMapaEmocional.Should().BeNull();
        original.Estrategia!.FatorDecisao.Should().BeNull();
    }
}
