using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepTemplateEmailTests
{
    private const string Template = """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <meta http-equiv="X-UA-Compatible" content="IE=edge">
        <title>{{assunto}}</title>
        <!--[if mso]>
        <style type="text/css">
        table {border-collapse: collapse; border-spacing: 0;}
        td {font-family: Arial, sans-serif;}
        </style>
        <![endif]-->
        </head>
        <body style="margin: 0; padding: 0; background-color: #f4f4f4; -webkit-text-size-adjust: none; -ms-text-size-adjust: none;">
        <span style="display: none; max-height: 0; overflow: hidden; mso-hide: all; font-size: 1px; line-height: 1px; color: #ffffff;">{{preheader}}</span>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color: #f4f4f4;">
        <tr>
        <td align="center" style="padding: 20px 10px;">
        <!--[if mso]>
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0"><tr><td>
        <![endif]-->
        <table role="presentation" width="100%" style="max-width: 600px;" cellpadding="0" cellspacing="0" border="0">
        <tr>
        <td style="background-color: #ffffff; padding: 30px 20px 10px 20px; text-align: center;">
        <img src="{{logo_src}}" alt="Logo" border="0" style="display: block; max-width: 200px; height: auto; width: 200px;">
        </td>
        </tr>
        <tr>
        <td style="padding: 10px 30px 10px 30px;">
        <h1 style="margin: 0 0 15px 0; font-family: Arial, Helvetica, sans-serif; font-size: 24px; line-height: 1.3; color: #333333;">{{titulo}}</h1>
        </td>
        </tr>
        <tr>
        <td style="background-color: #ffffff; padding: 30px 20px 10px 20px; text-align: center;">
        <img src="{{logo_src}}" alt="Logo" border="0" style="display: block; max-width: 200px; height: auto; width: 200px;">
        </td>
        </tr>
        {{banner_section}}
        {{hero_section}}
        <tr>
        <td align="center" style="padding: 20px 30px 30px 30px;">
        <table role="presentation" cellpadding="0" cellspacing="0" border="0">
        <tr>
        <td style="background-color: #0070f3; border-radius: 4px; text-align: center;">
        <a href="{{cta_link}}" target="_blank" style="display: inline-block; padding: 14px 30px; font-family: Arial, Helvetica, sans-serif; font-size: 16px; color: #ffffff; text-decoration: none; font-weight: bold; border-radius: 4px;">{{cta_texto}}</a>
        </td>
        </tr>
        </table>
        </td>
        </tr>
        <tr>
        <td style="padding: 20px 30px 30px 30px; text-align: center; border-top: 1px solid #eeeeee;">
        <p style="margin: 0; font-family: Arial, Helvetica, sans-serif; font-size: 12px; line-height: 1.5; color: #999999;">Rodape fixo do template.</p>
        </td>
        </tr>
        </table>
        <!--[if mso]>
        </td></tr></table>
        <![endif]-->
        </td>
        </tr>
        </table>
        </body>
        </html>
        """;

    private const string HeroTr = """
        <tr>
        <td align="center" style="padding: 10px 30px;">
        <img src="{{hero_src}}" alt="Hero" border="0" style="display: block; max-width: 100%; height: auto; width: 100%;">
        </td>
        </tr>
        """;

    private const string BannerTr = """
        <tr>
        <td align="center" style="padding: 0;">
        <img src="{{banner_src}}" alt="Banner" border="0" style="display: block; max-width: 100%; height: auto; width: 100%;">
        </td>
        </tr>
        """;

    private const string HeaderTr = """
        <tr>
        <td width="600" style="{{header_bg}} {{header_clip}}">
        {{regua_html}}
        <table role="presentation" width="100%"><tr>{{header_coluna_1}}{{header_coluna_2}}</tr></table>
        </td>
        </tr>
        """;

    private const string TemplateComHeader = """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <body>
        <table width="600">{{banner_section}}{{header_section}}</table>
        </body>
        </html>
        """;

    private static PipelineContext CriarContexto(
        CopyEmailSlots? copy = null,
        string? logoSrc = null,
        string? heroSrc = null,
        string? bannerSrc = null,
        string? cliente = null)
    {
        copy ??= new CopyEmailSlots(
            "Assunto teste",
            "Preheader teste",
            "Titulo teste",
            "<p>Corpo do email com <strong>destaque</strong>.</p>",
            "Clique aqui",
            "https://exemplo.com");

        return new PipelineContext
        {
            ChatId = 123,
            Cliente = cliente,
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            MensagemOriginal = "msg",
            Copy = copy,
            LogoSrc = logoSrc,
            HeroSrc = heroSrc,
            BannerSrc = bannerSrc
        };
    }

    private static StepTemplateEmail CriarStep(
        string? templateOverride = null,
        IReferenciasCliente? referencias = null)
    {
        var catalogo = new Mock<ITemplateCatalogo>();
        var template = templateOverride ?? Template;
        catalogo.Setup(c => c.Obter(It.IsAny<string>())).Returns(template);
        catalogo.Setup(c => c.Default).Returns(template);
        
        var referenciasMock = referencias != null 
            ? Mock.Get(referencias)
            : new Mock<IReferenciasCliente>();
        
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var loggerMock = new Mock<ILogger<StepTemplateEmail>>();
        
        return new StepTemplateEmail(
            catalogo.Object,
            referenciasMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task ExecutarAsync_WithAllSlots_FillsAllPlaceholders()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().NotBeNullOrEmpty();
        ctx.Html.Should().NotContain("{{assunto}}");
        ctx.Html.Should().NotContain("{{preheader}}");
        ctx.Html.Should().NotContain("{{titulo}}");
        ctx.Html.Should().NotContain("{{corpo}}");
        ctx.Html.Should().NotContain("{{cta_link}}");
        ctx.Html.Should().NotContain("{{cta_texto}}");
        ctx.Html.Should().NotContain("{{logo_src}}");
        ctx.Html.Should().NotContain("{{hero_section}}");
        ctx.Html.Should().NotContain("{{hero_src}}");
        ctx.Html.Should().NotContain("{{banner_section}}");
        ctx.Html.Should().NotContain("{{banner_src}}");
        ctx.Resultado.RespostaFinal.Should().Be(ctx.Html);
    }

    [Fact]
    public async Task ExecutarAsync_Structure_UsesTableLayout()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("<table");
        ctx.Html.Should().NotContain("<div", "email HTML deve usar table layout, nao div");
        ctx.Html.Should().NotContain("display: flex", "email HTML nao deve usar flexbox");
        ctx.Html.Should().NotContain("display: grid", "email HTML nao deve usar grid");
    }

    [Fact]
    public async Task ExecutarAsync_Structure_HasGhostTables()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("<!--[if mso]>", "deve conter ghost tables para Outlook");
        ctx.Html.Should().Contain("<![endif]-->");
    }

    [Fact]
    public async Task ExecutarAsync_Structure_CssInline()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("style=\"", "CSS deve ser inline em cada tag");
        ctx.Html.Should().Contain("<td style=");
    }

    [Fact]
    public async Task ExecutarAsync_Structure_ImgHasAltBorderDisplayBlock()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("alt=\"Logo\"");
        ctx.Html.Should().Contain("border=\"0\"");
        ctx.Html.Should().Contain("style=\"display: block;");
    }

    [Fact]
    public async Task ExecutarAsync_Structure_MaxWidth600()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("max-width: 600px");
    }

    [Fact]
    public async Task ExecutarAsync_Structure_CtaBulletproofTable()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        var ctaIndex = ctx.Html!.IndexOf("Clique aqui");
        ctaIndex.Should().BeGreaterThan(0);
        var tableBeforeCta = ctx.Html.LastIndexOf("<table", ctaIndex);
        tableBeforeCta.Should().BeGreaterThan(0, "CTA deve ser uma tabela bulletproof");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutHero_HeroSectionEmpty()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png", heroSrc: null);

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().NotContain("alt=\"Hero\"");
    }

    [Fact]
    public async Task ExecutarAsync_WithHero_HeroSectionPresent()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png", heroSrc: "imagens/gerada_1.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("alt=\"Hero\"");
        ctx.Html.Should().Contain("src=\"imagens/gerada_1.png\"");
    }

    [Fact]
    public async Task ExecutarAsync_WithBanner_BannerSectionPresent()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");
        ctx.BannerSrc = "assets/agendar_vistoria.png";

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("alt=\"Banner\"");
        ctx.Html.Should().Contain("src=\"assets/agendar_vistoria.png\"");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutBanner_BannerSectionEmpty()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().NotContain("alt=\"Banner\"");
    }

    [Fact]
    public async Task ExecutarAsync_WithBannerAndHero_BothPresent()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png", heroSrc: "imagens/gerada_1.png");
        ctx.BannerSrc = "assets/agendar_vistoria.png";

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("alt=\"Banner\"");
        ctx.Html.Should().Contain("alt=\"Hero\"");
    }

    [Fact]
    public async Task ExecutarAsync_SlotsWithHtmlChars_EscapedProperly()
    {
        var copy = new CopyEmailSlots(
            "Assunto <especial> & \"citado\"",
            "Preheader",
            "Titulo",
            "<p>Corpo</p>",
            "CTA",
            "https://ex.com?a=1&b=2");

        var step = CriarStep();
        var ctx = CriarContexto(copy: copy, logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("Assunto &lt;especial&gt; &amp; &quot;citado&quot;");
        ctx.Html.Should().Contain("https://ex.com?a=1&amp;b=2");
    }

    [Fact]
    public async Task ExecutarAsync_SetsResultadoRespostaFinal()
    {
        var step = CriarStep();
        var ctx = CriarContexto(logoSrc: "assets/logo.png");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Resultado.RespostaFinal.Should().Be(ctx.Html);
        ctx.Resultado.RespostaFinal.Should().Contain("<!DOCTYPE html>");
    }

    [Fact]
    public async Task ExecutarAsync_WithFixedAssets_PopulatesFixedAssetRefs()
    {
        const string templateWithFixedAssets = """
            <html>
            <body>
            <img src="assets/thumb-up.png" alt="Like">
            <img src="assets/thumb-down.png" alt="Dislike">
            <img src="assets/instagram.png" alt="Instagram">
            {{banner_section}}
            {{hero_section}}
            </body>
            </html>
            """;
        var step = CriarStep(templateWithFixedAssets);
        var ctx = CriarContexto();

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.FixedAssetRefs.Should().Contain("thumb-up.png");
        ctx.FixedAssetRefs.Should().Contain("thumb-down.png");
        ctx.FixedAssetRefs.Should().Contain("instagram.png");
        ctx.FixedAssetRefs.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecutarAsync_WithoutFixedAssets_FixedAssetRefsIsEmpty()
    {
        const string templateWithoutAssets = """
            <html>
            <body>
            <p>No assets here</p>
            {{banner_section}}
            {{hero_section}}
            </body>
            </html>
            """;
        var step = CriarStep(templateWithoutAssets);
        var ctx = CriarContexto();

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.FixedAssetRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_WithFooterPlaceholder_InjectsFooterHtml()
    {
        const string templateWithFooter = """
            <html>
            <body>
            <p>Content</p>
            {{footer}}
            </body>
            </html>
            """;
        
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", null)).Returns("<footer>Default Footer</footer>");
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var step = CriarStep(templateWithFooter, referenciasMock.Object);
        var ctx = CriarContexto(cliente: "acme");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("<footer>Default Footer</footer>");
        ctx.Html.Should().NotContain("{{footer}}");
    }

    [Fact]
    public async Task ExecutarAsync_WithNamedFooterPlaceholder_InjectsNamedFooterHtml()
    {
        const string templateWithNamedFooter = """
            <html>
            <body>
            <p>Content</p>
            {{footer:mrveco}}
            </body>
            </html>
            """;
        
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", "mrveco")).Returns("<footer>MRV&CO Footer</footer>");
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var step = CriarStep(templateWithNamedFooter, referenciasMock.Object);
        var ctx = CriarContexto(cliente: "acme");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("<footer>MRV&CO Footer</footer>");
        ctx.Html.Should().NotContain("{{footer:mrveco}}");
    }

    [Fact]
    public async Task ExecutarAsync_WithMissingFooter_RemovesPlaceholder()
    {
        const string templateWithFooter = """
            <html>
            <body>
            <p>Content</p>
            {{footer}}
            </body>
            </html>
            """;
        
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", null)).Returns((string?)null);
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var step = CriarStep(templateWithFooter, referenciasMock.Object);
        var ctx = CriarContexto(cliente: "acme");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().NotContain("{{footer}}");
        ctx.Html.Should().NotContain("<footer>");
    }

    [Fact]
    public async Task ExecutarAsync_WithMultipleNamedFooters_InjectsAll()
    {
        const string templateWithMultipleFooters = """
            <html>
            <body>
            <p>Content</p>
            {{footer:default}}
            <div>Middle</div>
            {{footer:special}}
            </body>
            </html>
            """;
        
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", "default")).Returns("<footer>Default</footer>");
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", "special")).Returns("<footer>Special</footer>");
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var step = CriarStep(templateWithMultipleFooters, referenciasMock.Object);
        var ctx = CriarContexto(cliente: "acme");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("<footer>Default</footer>");
        ctx.Html.Should().Contain("<footer>Special</footer>");
        ctx.Html.Should().NotContain("{{footer:default}}");
        ctx.Html.Should().NotContain("{{footer:special}}");
    }

    [Fact]
    public async Task ExecutarAsync_WithFooterContainingAssets_IncludesInFixedAssetRefs()
    {
        const string templateWithFooter = """
            <html>
            <body>
            <p>Content</p>
            {{footer}}
            </body>
            </html>
            """;
        
        const string footerWithAssets = """
            <footer>
            <img src="assets/instagram.png" alt="Instagram">
            <img src="assets/facebook.png" alt="Facebook">
            </footer>
            """;
        
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(r => r.ObterFooterHtml("acme", null)).Returns(footerWithAssets);
        referenciasMock.Setup(r => r.ObterHeroSectionTemplate()).Returns(HeroTr);
        referenciasMock.Setup(r => r.ObterBannerSectionTemplate()).Returns(BannerTr);
        referenciasMock.Setup(r => r.ObterHeaderSectionTemplate()).Returns(HeaderTr);
        
        var step = CriarStep(templateWithFooter, referenciasMock.Object);
        var ctx = CriarContexto(cliente: "acme");

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.FixedAssetRefs.Should().Contain("instagram.png");
        ctx.FixedAssetRefs.Should().Contain("facebook.png");
    }

    private static PipelineContext CriarContextoComHeader(string? heroSrc = "imagens/gerada_1.png")
    {
        var ctx = CriarContexto(logoSrc: "assets/logo.png", heroSrc: heroSrc);
        ctx.Estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            FaseDados = new FaseEstrategia
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
            },
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
                    ["pos-compra"] = "Financiamento com o banco",
                    ["pre-chaves"] = "Vistoria",
                    ["pos-chaves"] = "Entrega das chaves"
                }
            }
        };
        return ctx;
    }

    [Fact]
    public async Task ExecutarAsync_WithHeaderSlotAndHero_RendersHtmlHeader()
    {
        var step = CriarStep(TemplateComHeader);
        var ctx = CriarContextoComHeader();
        ctx.HeaderLayoutInvertido = false;

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().Contain("background-color: #784099");
        ctx.Html.Should().Contain("linear-gradient(135deg, #784099 0%, #AB40D9 100%)");
        ctx.Html.Should().Contain(HeaderEmailBuilder.ClipPathPadrao);
        ctx.Html.Should().Contain("Assinatura do Contrato");
        ctx.Html.Should().Contain("Financiamento com o banco");
        ctx.Html.Should().Contain("Vistoria");
        ctx.Html.Should().Contain("Entrega das chaves");
        ctx.Html.Should().Contain("background-color: #FFDD02", "a etapa atual deve aparecer no amarelo da regua");
        ctx.Html.Should().Contain("Titulo teste");
        ctx.Html.Should().Contain("color: #FFFFFF");
        ctx.Html.Should().Contain("src=\"imagens/gerada_1.png\"");
        ctx.Html.Should().Contain("width=\"240\"", "o hero deve ser menor que o background de 600px");
        ctx.Html.Should().NotContain("{{header_section}}");
        ctx.Html.Should().NotContain("{{");
    }

    [Fact]
    public async Task ExecutarAsync_WithHeaderSlotWithoutHero_HeaderIsEmpty()
    {
        var step = CriarStep(TemplateComHeader);
        var ctx = CriarContextoComHeader(heroSrc: null);

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.Html.Should().NotContain("clip-path");
        ctx.Html.Should().NotContain("<img");
        ctx.Html.Should().NotContain("{{header_section}}");
    }

    [Fact]
    public async Task ExecutarAsync_WithDefaultHeaderLayout_TitleComesBeforeImage()
    {
        var step = CriarStep(TemplateComHeader);
        var ctx = CriarContextoComHeader();
        ctx.HeaderLayoutInvertido = false;

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        var tituloIndex = ctx.Html!.IndexOf("Titulo teste", StringComparison.Ordinal);
        var imgIndex = ctx.Html.IndexOf("<img", StringComparison.Ordinal);
        tituloIndex.Should().BeGreaterThan(0);
        tituloIndex.Should().BeLessThan(imgIndex, "no layout padrao o titulo fica a esquerda");
        ctx.Html.Should().Contain("align=\"left\"");
    }

    [Fact]
    public async Task ExecutarAsync_WithInvertedHeaderLayout_ImageComesBeforeTitle()
    {
        var step = CriarStep(TemplateComHeader);
        var ctx = CriarContextoComHeader();
        ctx.HeaderLayoutInvertido = true;

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        var imgIndex = ctx.Html!.IndexOf("<img", StringComparison.Ordinal);
        var tituloIndex = ctx.Html.IndexOf("Titulo teste", StringComparison.Ordinal);
        imgIndex.Should().BeGreaterThan(0);
        imgIndex.Should().BeLessThan(tituloIndex, "no layout invertido a imagem fica a esquerda");
        ctx.Html.Should().Contain("align=\"right\"");
        ctx.Html.Should().Contain(HeaderEmailBuilder.ClipPathInvertido);
    }

    [Fact]
    public async Task ExecutarAsync_HeaderLayout_IsAssignedOnce()
    {
        var step = CriarStep(TemplateComHeader);
        var ctx = CriarContextoComHeader();
        ctx.HeaderLayoutInvertido.Should().BeNull();

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        ctx.HeaderLayoutInvertido.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WithHeaderAndHeroSlots_DoesNotDuplicateHero()
    {
        const string templateComAmbosSlots = """
            <html>
            <body>
            <table>{{header_section}}{{hero_section}}</table>
            </body>
            </html>
            """;
        var step = CriarStep(templateComAmbosSlots);
        var ctx = CriarContextoComHeader();

        ctx = await step.ExecutarAsync(ctx, CancellationToken.None);

        var ocorrencias = System.Text.RegularExpressions.Regex.Matches(
            ctx.Html!, "src=\"imagens/gerada_1.png\"").Count;
        ocorrencias.Should().Be(1);
    }
}
