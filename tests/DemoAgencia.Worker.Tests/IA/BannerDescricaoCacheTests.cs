using System.Text.Json;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class BannerDescricaoCacheTests
{
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly string _tempDir;

    public BannerDescricaoCacheTests()
    {
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"banner_cache_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenSidecarExists_ShouldReturnCachedWithoutCallingAnalyzer()
    {
        var bannerPath = Path.Combine(_tempDir, "MRV_test_banner.png");
        await File.WriteAllBytesAsync(bannerPath, new byte[] { 0x89, 0x50 });

        var sidecarPath = bannerPath + ".desc.json";
        var cached = new BannerDescricao("Cached desc", "Central", new List<string> { "#006b40" }, "Flat", "Acolhedor", "");
        await File.WriteAllTextAsync(sidecarPath, JsonSerializer.Serialize(cached, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        var cache = new BannerDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<BannerDescricaoCache>>());
        var banner = new AssetVisual { Caminho = bannerPath };

        var result = await cache.ObterDescricaoAsync(banner, CancellationToken.None);

        result.DescricaoGeral.Should().Be("Cached desc");
        result.PaletaDominante.Should().Contain("#006b40");
        _analisadorMock.Verify(a => a.DescreverBannerAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenNoSidecar_ShouldCallAnalyzerAndWriteSidecar()
    {
        var bannerPath = Path.Combine(_tempDir, "MRV_new_banner.png");
        await File.WriteAllBytesAsync(bannerPath, new byte[] { 0x89, 0x50 });

        var expected = new BannerDescricao("New desc", "Left-aligned", new List<string> { "#F48421" }, "Foto", "Urgente", "Agende");
        _analisadorMock.Setup(a => a.DescreverBannerAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var cache = new BannerDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<BannerDescricaoCache>>());
        var banner = new AssetVisual { Caminho = bannerPath };

        var result = await cache.ObterDescricaoAsync(banner, CancellationToken.None);

        result.DescricaoGeral.Should().Be("New desc");
        _analisadorMock.Verify(a => a.DescreverBannerAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        var sidecarPath = bannerPath + ".desc.json";
        File.Exists(sidecarPath).Should().BeTrue();
        var written = await File.ReadAllTextAsync(sidecarPath);
        written.Should().Contain("New desc");
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenSidecarWriteFails_ShouldReturnDescriptionAnyway()
    {
        var readOnlyDir = Path.Combine(_tempDir, "readonly_test");
        Directory.CreateDirectory(readOnlyDir);
        var bannerPath = Path.Combine(readOnlyDir, "test.png");
        await File.WriteAllBytesAsync(bannerPath, new byte[] { 0x89, 0x50 });

        var expected = new BannerDescricao("Desc", "", new List<string>(), "", "", "");
        _analisadorMock.Setup(a => a.DescreverBannerAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var cache = new BannerDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<BannerDescricaoCache>>());
        var banner = new AssetVisual { Caminho = bannerPath };

        var result = await cache.ObterDescricaoAsync(banner, CancellationToken.None);

        result.Should().NotBeNull();
        result.DescricaoGeral.Should().Be("Desc");
    }
}
