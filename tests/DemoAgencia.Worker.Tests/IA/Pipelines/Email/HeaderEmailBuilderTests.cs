using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class HeaderEmailBuilderTests
{
    private static FaseEstrategia FaseRoxa() => new()
    {
        Fase = "pos-compra",
        CorPrincipal = "Roxo",
        CoresHex = new List<List<string>>
        {
            new() { "gradiente", "linear", "diagonal", "#006B40", "#039E56", "#006B40" },
            new() { "gradiente", "linear", "diagonal", "#F48421", "#FFB719" },
            new() { "#784099" },
            new() { "#AB40D9" },
            new() { "#AB40D9" }
        }
    };

    private static PipelineContext ContextoHeader()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            MensagemOriginal = "msg",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            HeroSrc = "imagens/gerada_1.png"
        };
        context.Copy = new CopyEmailSlots(
            "Assunto",
            "Preheader",
            "Titulo <forte>",
            "<p>Corpo</p>",
            "CTA",
            "https://exemplo.com");
        context.Estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            FaseDados = FaseRoxa(),
            Regua = new ReguaJornada
            {
                Etapas = new List<string>
                {
                    "Assinatura do Contrato",
                    "Financiamento com o banco",
                    "Visita a Obra",
                    "Vistoria",
                    "Entrega das chaves"
                },
                EtapaDestacadaPorFase = new Dictionary<string, string>
                {
                    ["pos-compra"] = "Financiamento com o banco"
                }
            }
        };
        return context;
    }

    [Fact]
    public void ConstruirFundo_ComCoresDaFase_UsaGradienteDaEtapa()
    {
        var css = HeaderEmailBuilder.ConstruirFundo(FaseRoxa());

        css.Should().Contain("background-color: #784099");
        css.Should().Contain("linear-gradient(135deg, #784099 0%, #AB40D9 100%)");
        css.Should().NotContain("#006B40");
        css.Should().NotContain("#F48421");
    }

    [Fact]
    public void ConstruirFundo_SemCoresPropriasDaFase_UsaFallbackDaPaleta()
    {
        var fase = new FaseEstrategia
        {
            Fase = "pos-compra",
            CoresHex = new List<List<string>>
            {
                new() { "gradiente", "linear", "diagonal", "#006B40", "#039E56", "#006B40" },
                new() { "gradiente", "linear", "diagonal", "#F48421", "#FFB719" }
            }
        };

        var css = HeaderEmailBuilder.ConstruirFundo(fase);

        css.Should().Contain("background-color: #006B40");
        css.Should().Contain("linear-gradient(135deg, #006B40 0%, #039E56 100%)");
    }

    [Fact]
    public void ConstruirFundo_ComUmaCor_UsaCorSolida()
    {
        var fase = new FaseEstrategia
        {
            Fase = "pos-compra",
            CoresHex = new List<List<string>> { new() { "#784099" }, new() { "#784099" } }
        };

        var css = HeaderEmailBuilder.ConstruirFundo(fase);

        css.Should().Be("background-color: #784099;");
    }

    [Fact]
    public void ConstruirFundo_SemFase_RetornaVazio()
    {
        HeaderEmailBuilder.ConstruirFundo(null).Should().BeEmpty();
    }

    [Fact]
    public void ExtrairCoresFase_IgnoraGradientesFixosDaMarca()
    {
        var cores = HeaderEmailBuilder.ExtrairCoresFase(FaseRoxa());

        cores.Should().Equal("#784099", "#AB40D9");
    }

    [Fact]
    public void Construir_PreencheTodosOsPlaceholders()
    {
        const string componente = "<td style=\"{{header_bg}} {{header_clip}}\">{{regua_html}}{{header_coluna_1}}{{header_coluna_2}}</td>";

        var html = HeaderEmailBuilder.Construir(componente, ContextoHeader(), invertido: false);

        html.Should().NotContain("{{");
        html.Should().Contain("background-color: #784099");
        html.Should().Contain("Financiamento com o banco");
        html.Should().Contain("Titulo &lt;forte&gt;");
        html.Should().Contain("src=\"imagens/gerada_1.png\"");
        html.Should().Contain("width=\"240\"");
        html.Should().Contain(HeaderEmailBuilder.ClipPathPadrao);
    }

    [Fact]
    public void Construir_ComLayoutInvertido_EspelhaClipEColunas()
    {
        const string componente = "<td style=\"{{header_bg}} {{header_clip}}\">{{regua_html}}{{header_coluna_1}}{{header_coluna_2}}</td>";

        var html = HeaderEmailBuilder.Construir(componente, ContextoHeader(), invertido: true);

        html.Should().Contain(HeaderEmailBuilder.ClipPathInvertido);
        var imgIndex = html.IndexOf("<img", StringComparison.Ordinal);
        var tituloIndex = html.IndexOf("Titulo", StringComparison.Ordinal);
        imgIndex.Should().BeLessThan(tituloIndex);
        html.Should().Contain("align=\"right\"");
    }
}
