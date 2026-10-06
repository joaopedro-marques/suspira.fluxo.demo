using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepImagemHeroTests
{
    private readonly Mock<IServicoChat> _chatMock;
    private readonly Mock<IGeradorImagem> _geradorMock;
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly Mock<ITemplateCatalogo> _catalogoMock;
    private readonly Mock<IBannerDescricaoCache> _bannerCacheMock;
    private readonly Mock<IIconDescricaoCache> _iconCacheMock;
    private readonly StepImagemHero _step;

    public StepImagemHeroTests()
    {
        _chatMock = new Mock<IServicoChat>();
        _geradorMock = new Mock<IGeradorImagem>();
        _refsMock = new Mock<IReferenciasCliente>();
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _catalogoMock = new Mock<ITemplateCatalogo>();
        _bannerCacheMock = new Mock<IBannerDescricaoCache>();
        _iconCacheMock = new Mock<IIconDescricaoCache>();
        _iconCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IconDescricao("Icone teste", new List<string> { "teste" }, "Flat"));

        var agente = new AgenteDefinicao("hero", "test/model", 0.7, 1000, "persona");
        var curador = new AgenteDefinicao("curador", "test/model", 0.2, 1000, "curador persona");
        _step = new StepImagemHero(agente, curador, _chatMock.Object, _geradorMock.Object, _refsMock.Object, _analisadorMock.Object, _catalogoMock.Object, _bannerCacheMock.Object, _iconCacheMock.Object);
    }

    [Fact]
    public async Task ConstruirPromptBase_WithEstrategia_ShouldIncludePalette()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>());

        var estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            FaseDados = new FaseEstrategia
            {
                Fase = "pos-compra",
                CorPrincipal = "Roxo",
                DescricaoCor = "Transformacao e sonho",
                CoresHex = new List<List<string>> { new() { "#784099" }, new() { "#AB40D9" } }
            }
        };

        var prompt = await _step.ConstruirPromptBase("A professional banner", "mrv", estrategia, CancellationToken.None);

        prompt.Should().Contain("Color palette");
        prompt.Should().Contain("Roxo");
        prompt.Should().Contain("Transformacao");
        prompt.Should().Contain("#784099");
        prompt.Should().Contain("#AB40D9");
        prompt.Should().Contain("pos-compra");
    }

    [Fact]
    public async Task ConstruirPromptBase_WithoutEstrategia_ShouldNotIncludePalette()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>());

        var prompt = await _step.ConstruirPromptBase("A professional banner", "mrv", null, CancellationToken.None);

        prompt.Should().NotContain("Color palette");
        prompt.Should().Be("A professional banner");
    }

    [Fact]
    public async Task ConstruirPromptBase_WithoutCliente_ShouldReturnDescription()
    {
        var prompt = await _step.ConstruirPromptBase("A professional banner", null, null, CancellationToken.None);

        prompt.Should().Be("A professional banner");
    }

    [Fact]
    public async Task ExecutarAsync_WithCompatibleBanner_ShouldAttachBanner()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var bannerAsset = new AssetVisual
        {
            Cliente = "mrv",
            Tipo = TipoAsset.Banner,
            Nome = "agendar_vistoria",
            Caminho = tempFile
        };

        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual> { bannerAsset });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner de agendamento de vistoria", "", new List<string>(), "", "", "Agende sua vistoria"));

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compativel\": true, \"motivo\": \"Tema compativel\"}");

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "quero agendar vistoria",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            TemplateId = "MRV_html_limite_vistoria",
            Estrategia = new EstrategiaEmail { Fase = "pre-chaves", SubJornada = "vistoria" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().Be("assets/agendar_vistoria.png");
        context.HeroSrc.Should().BeNull();
        context.Resultado.AssetsAnexados.Should().HaveCount(1);
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_WithFirstIncompatible_SecondCompatible_ShouldUseSecond()
    {
        var tempFile1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        var tempFile2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile1, new byte[] { 0x89, 0x50 });
        await File.WriteAllBytesAsync(tempFile2, new byte[] { 0x89, 0x50 });

        var banner1 = new AssetVisual { Cliente = "mrv", Tipo = TipoAsset.Banner, Nome = "agendar_vistoria", Caminho = tempFile1 };
        var banner2 = new AssetVisual { Cliente = "mrv", Tipo = TipoAsset.Banner, Nome = "entrega_chaves", Caminho = tempFile2 };

        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual> { banner1, banner2 });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.Is<AssetVisual>(a => a.Nome == "agendar_vistoria"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner de agendamento de vistoria", "", new List<string>(), "", "", "Agende vistoria"));
        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.Is<AssetVisual>(a => a.Nome == "entrega_chaves"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner de entrega de chaves", "", new List<string>(), "", "", "Parabens pela entrega"));

        var callCount = 0;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? "{\"compativel\": false, \"motivo\": \"Banner de vistoria nao condiz com assembleia\"}"
                    : "{\"compativel\": true, \"motivo\": \"Banner de entrega de chaves compativel\"}";
            });

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia de condominio",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra", SubJornada = "assembleia" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().Be("assets/entrega_chaves.png");
        context.HeroSrc.Should().BeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_AllBannersIncompatible_WithHeroBrief_ShouldGenerateImage()
    {
        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual>
            {
                new() { Cliente = "mrv", Tipo = TipoAsset.Banner, Nome = "agendar_vistoria", Caminho = Path.GetTempFileName() }
            });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner de vistoria", "", new List<string>(), "", "", "Vistoria"));

        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>());

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compativel\": false, \"motivo\": \"Tema incompativel\"}");

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_hero_prompt", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A hero image prompt");

        _geradorMock.Setup(g => g.GerarImagemAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 0x89, 0x50 }, null));

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia de condominio",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(),
                new List<ImagemBrief> { new("hero", "Banner de assembleia") }),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().BeNull();
        context.HeroSrc.Should().NotBeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_AllBannersIncompatible_WithoutHeroBrief_ShouldGenerateWithDefaultPrompt()
    {
        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual>
            {
                new() { Cliente = "mrv", Tipo = TipoAsset.Banner, Nome = "agendar_vistoria", Caminho = Path.GetTempFileName() }
            });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner de vistoria", "", new List<string>(), "", "", "Vistoria"));

        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>());

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compativel\": false, \"motivo\": \"Tema incompativel\"}");

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_hero_prompt", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A default hero image prompt");

        _geradorMock.Setup(g => g.GerarImagemAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 0x89, 0x50 }, null));

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia de condominio",
            Brief = new Brief("email", "convocar moradores", null, "assembleia geral", null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia { Fase = "pos-compra", Temas = new List<string> { "assembleia", "condominio" } }
            }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().BeNull();
        context.HeroSrc.Should().NotBeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_WithoutMatchingBanner_ShouldGenerateHeroViaIA()
    {
        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual>());

        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>());

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A hero image prompt");

        _geradorMock.Setup(g => g.GerarImagemAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 0x89, 0x50 }, null));

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email sobre pintura",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(),
                new List<ImagemBrief> { new("hero", "A hero image") }),
            Estrategia = new EstrategiaEmail { Fase = "pos-chaves" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().BeNull();
        context.HeroSrc.Should().NotBeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_CuradorReturnsInvalidJson_ShouldFailOpenAndKeepBanner()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var bannerAsset = new AssetVisual
        {
            Cliente = "mrv",
            Tipo = TipoAsset.Banner,
            Nome = "agendar_vistoria",
            Caminho = tempFile
        };

        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual> { bannerAsset });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner generico", "", new List<string>(), "", "", ""));

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("this is not valid json");

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email generico",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().Be("assets/agendar_vistoria.png");
        context.HeroSrc.Should().BeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_CuradorThrows_ShouldFailOpenAndKeepBanner()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var bannerAsset = new AssetVisual
        {
            Cliente = "mrv",
            Tipo = TipoAsset.Banner,
            Nome = "agendar_vistoria",
            Caminho = tempFile
        };

        _refsMock.Setup(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<AssetVisual> { bannerAsset });

        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BannerDescricao("Banner generico", "", new List<string>(), "", "", ""));

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_banner_check", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email generico",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" }
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().Be("assets/agendar_vistoria.png");
        context.HeroSrc.Should().BeNull();
    }

    [Fact]
    public async Task ConstruirPromptBase_WithBanners_ShouldIncludeBannerDescriptions()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x89, 0x50 });

        var bannerAssets = new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Banner, Nome = "visita_tecnica", Caminho = tempFile }
        };

        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(bannerAssets);

        var bannerDesc = new BannerDescricao("Banner com casa", "Centralizado", new List<string> { "#006b40" }, "Flat", "Acolhedor", "Agende visita");
        _bannerCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bannerDesc);

        var prompt = await _step.ConstruirPromptBase("A hero image", "mrv", null, CancellationToken.None);

        prompt.Should().Contain("Visual identity references");
        prompt.Should().Contain("Banner com casa");
        prompt.Should().Contain("#006b40");
    }

    [Fact]
    public async Task ConstruirPromptBase_WithIcons_ShouldUseIconCache()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x89, 0x50 });

        var iconAssets = new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "casa", Caminho = tempFile }
        };

        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(iconAssets);

        var iconDesc = new IconDescricao("Icone de casa", new List<string> { "moradia", "imovel" }, "Flat line");
        _iconCacheMock.Setup(c => c.ObterDescricaoAsync(It.Is<AssetVisual>(a => a.Nome == "casa"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iconDesc);

        var prompt = await _step.ConstruirPromptBase("A hero image", "mrv", null, CancellationToken.None);

        prompt.Should().Contain("Visual identity references");
        prompt.Should().Contain("Icone de casa");
        prompt.Should().Contain("moradia");
        _iconCacheMock.Verify(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_WhenRefacaoHero_ShouldSkipBannerAndGenerateImage()
    {
        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>());

        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A revised hero image prompt");

        _geradorMock.Setup(g => g.GerarImagemAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 0x89, 0x50 }, null));

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email sobre pintura",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(),
                new List<ImagemBrief> { new("hero", "A hero image") }),
            Estrategia = new EstrategiaEmail { Fase = "pos-chaves" },
            Refacoes = 1,
            QaStepAlvo = "hero",
            QaFeedback = "Banner is inadequate for this content",
            BannerSrc = "assets/old_banner.png"
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.BannerSrc.Should().BeNull();
        context.HeroSrc.Should().NotBeNull();
        _geradorMock.Verify(g => g.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _refsMock.Verify(r => r.SelecionarBannersRanked(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void ParseCuradorResult_WithValidCompatible_ShouldReturnTrue()
    {
        var json = "{\"compativel\": true, \"motivo\": \"Tema ok\"}";

        var (compativel, motivo) = StepImagemHero.ParseCuradorResult(json);

        compativel.Should().BeTrue();
        motivo.Should().Be("Tema ok");
    }

    [Fact]
    public void ParseCuradorResult_WithValidIncompatible_ShouldReturnFalse()
    {
        var json = "{\"compativel\": false, \"motivo\": \"Tema diferente\"}";

        var (compativel, _) = StepImagemHero.ParseCuradorResult(json);

        compativel.Should().BeFalse();
    }

    [Fact]
    public void ParseCuradorResult_WithInvalidJson_ShouldReturnNull()
    {
        var (compativel, _) = StepImagemHero.ParseCuradorResult("not json at all");

        compativel.Should().BeNull();
    }

    [Fact]
    public void ConstruirDescricaoDefault_ShouldIncludeOfertaAndFase()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia",
            Brief = new Brief("email", "convocar", null, "assembleia geral", null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                FaseDados = new FaseEstrategia { Fase = "pos-compra", Temas = new List<string> { "assembleia" } }
            }
        };

        var descricao = StepImagemHero.ConstruirDescricaoDefault(context);

        descricao.Should().Contain("assembleia geral");
        descricao.Should().Contain("pos-compra");
        descricao.Should().Contain("assembleia");
    }

    [Fact]
    public void MontarPromptCurador_ShouldIncludeAllContext()
    {
        var descricao = new BannerDescricao("Banner de vistoria", "Centralizado", new List<string>(), "Flat", "Tecnico", "Agende vistoria");
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "assembleia de condominio",
            Brief = new Brief("email", "convocar moradores", null, "assembleia geral", null, null, new List<string>(), new List<ImagemBrief>()),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-compra",
                SubJornada = "assembleia",
                FaseDados = new FaseEstrategia { Fase = "pos-compra", Temas = new List<string> { "assembleia", "condominio" } }
            }
        };

        var prompt = StepImagemHero.MontarPromptCurador(descricao, context);

        prompt.Should().Contain("Banner de vistoria");
        prompt.Should().Contain("convocar moradores");
        prompt.Should().Contain("assembleia geral");
        prompt.Should().Contain("pos-compra");
        prompt.Should().Contain("assembleia");
        prompt.Should().Contain("condominio");
        prompt.Should().Contain("assembleia de condominio");
    }
}
