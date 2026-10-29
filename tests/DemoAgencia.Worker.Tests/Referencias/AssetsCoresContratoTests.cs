using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Referencias;

/// <summary>
/// Contrato dos assets reais: toda fase precisa declarar cor de texto e a regua
/// precisa ter override por fase. Falhar aqui significa que uma alteracao de asset
/// deixaria texto ou elementos invisiveis no header.
/// </summary>
public class AssetsCoresContratoTests
{
    private static string LocalizarReferencias()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidato = Path.Combine(dir.FullName, "Assets", "referencias");
            if (Directory.Exists(candidato))
                return candidato;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Assets/referencias nao encontrado a partir de {AppContext.BaseDirectory}");
    }

    private static async Task<ReferenciaClienteLoader> CarregarLoaderAsync()
    {
        var loader = new ReferenciaClienteLoader(
            Mock.Of<ILogger<ReferenciaClienteLoader>>(),
            Mock.Of<IConfiguration>(),
            LocalizarReferencias());

        await loader.StartAsync(CancellationToken.None);
        return loader;
    }

    private static async Task<EstrategiaCliente> CarregarMrvAsync()
    {
        var loader = await CarregarLoaderAsync();

        var estrategia = loader.ObterEstrategia("mrv");
        estrategia.Should().NotBeNull("a estrategia da MRV precisa ser carregada dos assets reais");
        return estrategia!;
    }

    [Fact]
    public async Task HeaderSection_DeclaraBlocosELayout()
    {
        var loader = await CarregarLoaderAsync();
        var componente = loader.ObterHeaderSectionTemplate();

        componente.Should().NotBeNull("o header_section.html precisa existir nos assets");
        componente.Should().Contain("{{header_colunas}}", "as colunas sao injetadas na ordem sorteada");
        componente.Should().Contain("<!--#bloco titulo-->");
        componente.Should().Contain("<!--#bloco hero-->");
        componente.Should().Contain("{{header_hero_src}}");
        componente.Should().Contain("{{header_titulo_cor}}");
        componente.Should().NotContain("{{header_moldura_cor}}", "a moldura do hero foi removida");
        componente.Should().NotContain("<!--#bloco coluna_1-->");
    }

    [Fact]
    public async Task HeaderSectionMrv_TemContornoBrancoSemPreenchimento()
    {
        var loader = await CarregarLoaderAsync();
        var componente = loader.ObterHeaderSectionTemplate("mrv");

        componente.Should().NotBeNull("a MRV tem header proprio (header_section_mrv.html)");
        componente.Should().Contain("border: 2px solid #FFFFFF", "o contorno e um quadrado sem preenchimento");
        componente.Should().Contain("padding: 12px 0 0 12px", "o contorno fica deslocado acima/esquerda da foto");
        componente.Should().NotContain("bgcolor=", "nao ha preenchimento atras do hero");
    }

    [Fact]
    public async Task HeaderDaMrv_RenderizaComCoresLegiveis()
    {
        var loader = await CarregarLoaderAsync();
        var estrategia = loader.ObterEstrategia("mrv")!;
        var componente = loader.ObterHeaderSectionTemplate("mrv")!;
        var fase = estrategia.Fases["pos-compra"];

        var contexto = new PipelineContext
        {
            ChatId = 1,
            MensagemOriginal = "msg",
            HeroSrc = "imagens/gerada_1.png",
            Copy = new CopyEmailSlots(
                "Assunto", "Preheader", "Titulo do header", "<p>corpo</p>", "CTA", "https://exemplo.com"),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = fase,
                Regua = estrategia.Regua
            }
        };

        var html = HeaderEmailBuilder.Construir(componente, contexto, invertido: false);

        html.Should().NotContain("{{", "todo slot do asset precisa ser resolvido");
        html.Should().NotContain("<!--#bloco", "os blocos sao extraidos do layout");
        html.Should().NotContain("bgcolor=", "o hero nao tem moldura");
        html.Should().Contain("border: 2px solid #FFFFFF", "o hero tem contorno sem preenchimento");
        html.Should().Contain("color: #FFFFFF", "o titulo do roxo e branco (asset)");
        html.Should().Contain("Titulo do header");
        html.Should().Contain("width=\"208\"");
    }

    [Fact]
    public async Task HeaderDaMrv_PosChaves_UsaHeroComoFundoFullBleed()
    {
        var loader = await CarregarLoaderAsync();
        var estrategia = loader.ObterEstrategia("mrv")!;
        var componente = loader.ObterHeaderSectionTemplate("mrv")!;
        var faseConfig = estrategia.Header!.PorFase["pos-chaves"];

        faseConfig.UsaHeroComoFundo.Should()
            .BeTrue("pos-chaves declara fundo hero no MRV_header.json");
        faseConfig.CorFundo.Should().Be("#1A1A1A", "sem background-image o header cai nessa cor");
        estrategia.Header.PorFase.Should().NotContainKey("pos-compra", "pos-compra segue com hero em coluna");

        var contexto = new PipelineContext
        {
            ChatId = 1,
            MensagemOriginal = "msg",
            HeroSrc = "imagens/gerada_1.png",
            Copy = new CopyEmailSlots(
                "Assunto", "Preheader", "Titulo", "<p>corpo</p>", "CTA", "https://exemplo.com"),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-chaves",
                FaseDados = estrategia.Fases["pos-chaves"],
                Regua = estrategia.Regua,
                Header = estrategia.Header
            }
        };

        var html = HeaderEmailBuilder.Construir(componente, contexto, invertido: false);

        html.Should().Contain("url('imagens/gerada_1.png')");
        html.Should().Contain("background-color: #1A1A1A;");
        html.Should().Contain("background-size: cover");
        html.Should().NotContain("<img", "a foto ocupa o fundo, nao uma coluna");
        html.Should().NotContain("{{");
        html.Should().NotContain("#2B1B00", "o texto do header nao usa mais a cor escura do fundo amarelo");
        html.Should().NotContain("#784099", "a regua nao usa mais o roxo do fundo amarelo");
        html.Should().Contain(
            "color: #FFDD02; font-weight: bold;\">Entrega das chaves",
            "a etapa atual da regua usa o destaque amarelo");
    }

    [Fact]
    public async Task HeaderConfig_DeclaraOrdemEClipeNoAsset()
    {
        var estrategia = await CarregarMrvAsync();
        var header = estrategia.Header;

        header.Should().NotBeNull("MRV_header.json precisa existir");
        header!.Colunas.Should().Equal("titulo", "hero");
        header.SortearOrdem.Should().BeTrue();
        header.RequerHero.Should().BeTrue();
        header.ClipPathPadrao.Should().StartWith("clip-path:");
        header.ClipPathInvertido.Should().StartWith("clip-path:");
        header.GradienteAngulo.Should().Be("135deg");
    }

    [Fact]
    public async Task Paleta_DeclaraCorDeTextoEmTodasAsFases()
    {
        var estrategia = await CarregarMrvAsync();
        var fasesComCor = estrategia.Fases.Values.Where(f => f.CoresHex.Count > 0).ToList();

        fasesComCor.Should().NotBeEmpty();
        foreach (var fase in fasesComCor)
        {
            fase.CorTextoHeader.Should().NotBeNullOrWhiteSpace($"{fase.Fase} precisa declarar cor_texto_header");
            CorHex.EhHexValido(fase.CorTextoHeader).Should().BeTrue($"{fase.Fase}: cor_texto_header invalida");
        }
    }

    [Fact]
    public async Task Paleta_TextoTemContrasteMinimoSobreOFundoEfetivo()
    {
        var estrategia = await CarregarMrvAsync();

        foreach (var fase in estrategia.Fases.Values.Where(f => f.CoresHex.Count > 0))
        {
            var fundos = FundosEfetivos(estrategia, fase);
            if (fundos.Count == 0)
                continue;

            var contrasteTexto = CorHex.ContrasteMinimo(fundos, fase.CorTextoHeader!);
            contrasteTexto.Should().BeGreaterThanOrEqualTo(
                CorHex.ContrasteMinimoAbsoluto,
                $"{fase.Fase}: titulo em {fase.CorTextoHeader} sobre {string.Join(", ", fundos)}");
        }
    }

    [Fact]
    public async Task ReguaSection_DeclaraBlocosECelulasNoAsset()
    {
        var loader = await CarregarLoaderAsync();
        var componente = loader.ObterReguaSectionTemplate();

        componente.Should().NotBeNull("o regua_section.html precisa existir nos assets");
        componente.Should().Contain("{{regua_etapas}}");
        componente.Should().Contain("<!--#bloco etapa-concluida-->");
        componente.Should().Contain("<!--#bloco etapa-atual-->");
        componente.Should().Contain("<!--#bloco etapa-futura-->");
        componente.Should().Contain("{{cor_esquerda}}");
        componente.Should().Contain("{{cor_direita}}");
        componente.Should().Contain("{{cor_marcador}}");
        componente.Should().Contain("{{cor_label}}");
        componente.Should().Contain("{{largura}}");
        componente.Should().Contain("{{label}}");
        componente.Should().Contain("border-radius", "as etapas usam marcadores circulares");
    }

    [Fact]
    public async Task Regua_DeclaraOverrideDeCorParaCadaFase()
    {
        var estrategia = await CarregarMrvAsync();
        var regua = estrategia.Regua;

        regua.Should().NotBeNull();

        regua!.EstiloHtmlPorFase.Keys.Should().BeEquivalentTo(
            regua.EtapaDestacadaPorFase.Keys,
            "cada fase tem um fundo de header diferente e precisa das proprias cores de regua");
    }

    [Fact]
    public async Task Regua_CoresPorFaseSaoLegiveisSobreOFundoEfetivo()
    {
        var estrategia = await CarregarMrvAsync();
        var regua = estrategia.Regua!;

        foreach (var (fase, estilo) in regua.EstiloHtmlPorFase)
        {
            estrategia.Fases.Should().ContainKey(fase);
            var fundos = FundosEfetivos(estrategia, estrategia.Fases[fase]);
            if (fundos.Count == 0)
                continue;

            CorHex.ContrasteMinimo(fundos, estilo.CorLabel).Should()
                .BeGreaterThanOrEqualTo(CorHex.ContrasteMinimoAbsoluto, $"{fase}: cor_label");
            CorHex.ContrasteMinimo(fundos, estilo.CorLabelAtual).Should()
                .BeGreaterThanOrEqualTo(CorHex.ContrasteMinimoAbsoluto, $"{fase}: cor_label_atual");
            CorHex.ContrasteMinimo(fundos, estilo.CorConcluida).Should()
                .BeGreaterThanOrEqualTo(CorHex.ContrasteMinimoDecorativo, $"{fase}: cor_concluida");
            CorHex.ContrasteMinimo(fundos, estilo.CorAtual).Should()
                .BeGreaterThanOrEqualTo(CorHex.ContrasteMinimoDecorativo, $"{fase}: cor_atual");
        }
    }

    /// <summary>
    /// Fundos efetivos do header para validar contraste: fases com fundo hero usam a cor
    /// solida de fallback (o que aparece sem background-image); as demais usam o gradiente.
    /// </summary>
    private static IReadOnlyCollection<string> FundosEfetivos(EstrategiaCliente estrategia, FaseEstrategia fase)
    {
        var faseHeader = estrategia.Header?.ObterFase(fase.Fase);
        if (faseHeader?.UsaHeroComoFundo == true && faseHeader.CorFundo is { } corFundo)
            return new[] { corFundo };

        return HeaderEmailBuilder.CoresGradiente(fase);
    }
}
