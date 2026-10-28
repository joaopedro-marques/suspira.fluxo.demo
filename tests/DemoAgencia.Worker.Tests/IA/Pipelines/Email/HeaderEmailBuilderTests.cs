using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class HeaderEmailBuilderTests
{
    private const string Componente = """
        <td style="{{header_bg}} {{header_clip}}">{{regua_html}}{{header_colunas}}</td>
        <!--#bloco titulo-->
        <td width="57%" align="{{header_titulo_align}}" style="{{header_titulo_padding}}"><p style="color: {{header_titulo_cor}};">{{header_titulo}}</p></td>
        <!--#/bloco-->
        <!--#bloco hero-->
        <td width="43%"><img src="{{header_hero_src}}" alt="Hero" width="220"></td>
        <!--#/bloco-->
        """;

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

    private static FaseEstrategia FaseAmarela() => new()
    {
        Fase = "pos-chaves",
        CorPrincipal = "Amarelo",
        CorTextoHeader = "#2B1B00",
        CoresHex = new List<List<string>>
        {
            new() { "gradiente", "linear", "diagonal", "#006B40", "#039E56", "#006B40" },
            new() { "gradiente", "linear", "diagonal", "#F48421", "#FFB719" },
            new() { "#FFB719" },
            new() { "#FFDD02" },
            new() { "#784099" }
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

        var html = HeaderEmailBuilder.Construir(Componente, ContextoHeader(), invertido: false);

        html.Should().NotContain("{{");
        html.Should().Contain("background-color: #784099");
        html.Should().Contain("Financiamento com o banco");
        html.Should().Contain("Titulo &lt;forte&gt;");
        html.Should().Contain("src=\"imagens/gerada_1.png\"");
        html.Should().Contain("width=\"220\"");
        html.Should().Contain(HeaderEmailBuilder.ClipPathPadrao);
    }

    [Fact]
    public void Construir_ComLayoutInvertido_EspelhaClipEColunas()
    {

        var html = HeaderEmailBuilder.Construir(Componente, ContextoHeader(), invertido: true);

        html.Should().Contain(HeaderEmailBuilder.ClipPathInvertido);
        var imgIndex = html.IndexOf("<img", StringComparison.Ordinal);
        var tituloIndex = html.IndexOf("Titulo", StringComparison.Ordinal);
        imgIndex.Should().BeLessThan(tituloIndex);
        html.Should().Contain("align=\"right\"");
    }

    [Fact]
    public void CorTitulo_ComCorDeclaradaIlegivel_MantemCorDoAsset()
    {
        var fase = FaseAmarela();
        fase.CorTextoHeader = "#FFFFFF";

        HeaderEmailBuilder.CorTitulo(fase).Should().Be("#FFFFFF", "a cor declarada no asset e usada como esta");
    }

    [Fact]
    public void CorTitulo_ComFaseAmarela_UsaCorEscuraDeclarada()
    {
        HeaderEmailBuilder.CorTitulo(FaseAmarela()).Should().Be("#2B1B00");
    }

    [Fact]
    public void CorTitulo_SemCorDeclaradaEmFundoClaro_UsaEscuro()
    {
        var fase = FaseAmarela();
        fase.CorTextoHeader = null;

        HeaderEmailBuilder.CorTitulo(fase).Should().Be(CorHex.TextoEscuroPadrao);
    }

    [Fact]
    public void CorTitulo_SemCorDeclaradaEmFundoEscuro_UsaBranco()
    {
        var fase = FaseRoxa();
        fase.CorTextoHeader = null;

        HeaderEmailBuilder.CorTitulo(fase).Should().Be(CorHex.TextoClaroPadrao);
    }

    [Fact]
    public void Construir_ComFaseAmarela_NaoUsaTituloBranco()
    {
        var contexto = ContextoHeader();
        contexto.Estrategia = new EstrategiaEmail
        {
            Fase = "pos-chaves",
            FaseDados = FaseAmarela(),
            Regua = contexto.Estrategia!.Regua
        };

        var html = HeaderEmailBuilder.Construir(Componente, contexto, invertido: false);

        html.Should().Contain("<p style=\"color: #2B1B00;\"", "o titulo usa a cor declarada do asset");
        html.Should().NotContain("<p style=\"color: #FFFFFF;\"", "o titulo nao pode ficar branco no fundo amarelo");
    }

    [Fact]
    public void ExtrairCoresFase_DescartaHexInvalido()
    {
        var fase = new FaseEstrategia
        {
            Fase = "pos-compra",
            CoresHex = new List<List<string>>
            {
                new() { "#78409" },
                new() { "#AB40D9" }
            }
        };

        HeaderEmailBuilder.ExtrairCoresFase(fase).Should().Equal("#AB40D9");
    }

    [Fact]
    public void Construir_UsaAnguloClipEOrdemDaConfiguracaoDoCliente()
    {
        var contexto = ContextoHeader();
        contexto.Estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            FaseDados = FaseRoxa(),
            Regua = contexto.Estrategia!.Regua,
            Header = new HeaderConfig
            {
                Colunas = new List<string> { "hero", "titulo" },
                SortearOrdem = false,
                GradienteAngulo = "90deg",
                ClipPathPadrao = "clip-path: polygon(0 0, 10px 0);"
            }
        };

        var html = HeaderEmailBuilder.Construir(Componente, contexto, invertido: false);

        html.Should().Contain("linear-gradient(90deg,");
        html.Should().Contain("clip-path: polygon(0 0, 10px 0);");
        html.IndexOf("<img", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("Titulo", StringComparison.Ordinal));
    }

    [Fact]
    public void Construir_SemHero_NaoRenderizaBlocoHero()
    {
        var contexto = ContextoHeader();
        contexto.HeroSrc = null;

        var html = HeaderEmailBuilder.Construir(Componente, contexto, invertido: false);

        html.Should().NotContain("<img");
        html.Should().Contain("Titulo &lt;forte&gt;");
        html.Should().NotContain("{{");
    }

    [Fact]
    public void MontarColunas_ComOrdemDeTresBlocos_NaoInverte()
    {
        var blocos = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B", ["c"] = "C" };

        HeaderEmailBuilder.MontarColunas(blocos, new[] { "a", "b", "c" }, invertido: true)
            .Should().Be("ABC");
    }

    [Fact]
    public void MontarColunas_ComDoisBlocosInvertido_TrocaAOrdem()
    {
        var blocos = new Dictionary<string, string> { ["titulo"] = "T", ["hero"] = "H" };

        HeaderEmailBuilder.MontarColunas(blocos, new[] { "titulo", "hero" }, invertido: true)
            .Should().Be("HT");
    }
}
