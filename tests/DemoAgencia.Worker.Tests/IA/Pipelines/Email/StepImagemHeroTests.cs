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
    private readonly StepImagemHero _step;

    public StepImagemHeroTests()
    {
        _chatMock = new Mock<IServicoChat>();
        _geradorMock = new Mock<IGeradorImagem>();
        _refsMock = new Mock<IReferenciasCliente>();
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _catalogoMock = new Mock<ITemplateCatalogo>();
        _bannerCacheMock = new Mock<IBannerDescricaoCache>();

        var agente = new DemoAgencia.Worker.Agentes.AgenteDefinicao("hero", "test/model", 0.7, 1000, "persona");
        _step = new StepImagemHero(agente, _chatMock.Object, _geradorMock.Object, _refsMock.Object, _analisadorMock.Object, _catalogoMock.Object, _bannerCacheMock.Object);
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
    public async Task ExecutarAsync_WithMatchingBanner_ShouldSkipImageGeneration()
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

        _refsMock.Setup(r => r.SelecionarBanner(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>()))
            .Returns(bannerAsset);

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
    public async Task ExecutarAsync_WithoutMatchingBanner_ShouldGenerateHeroViaIA()
    {
        _refsMock.Setup(r => r.SelecionarBanner(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>()))
            .Returns((AssetVisual?)null);

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
}
