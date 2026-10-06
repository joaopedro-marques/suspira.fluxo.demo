using System.Text.Json;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class IconDescricaoCacheTests
{
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly string _tempDir;

    public IconDescricaoCacheTests()
    {
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"icon_cache_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenSidecarExists_ShouldReturnCachedWithoutCallingAnalyzer()
    {
        var iconPath = Path.Combine(_tempDir, "MRV_casa.png");
        await File.WriteAllBytesAsync(iconPath, new byte[] { 0x89, 0x50 });

        var sidecarPath = iconPath + ".desc.json";
        var cached = new IconDescricao("Icone de casa", new List<string> { "moradia", "imovel" }, "Flat line");
        await File.WriteAllTextAsync(sidecarPath, JsonSerializer.Serialize(cached, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        var cache = new IconDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<IconDescricaoCache>>());
        var icon = new AssetVisual { Caminho = iconPath };

        var result = await cache.ObterDescricaoAsync(icon, CancellationToken.None);

        result.DescricaoGeral.Should().Be("Icone de casa");
        result.PalavrasChave.Should().Contain("moradia");
        _analisadorMock.Verify(a => a.DescreverIconeAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenNoSidecar_ShouldCallAnalyzerAndWriteSidecar()
    {
        var iconPath = Path.Combine(_tempDir, "MRV_telefone.png");
        await File.WriteAllBytesAsync(iconPath, new byte[] { 0x89, 0x50 });

        var expected = new IconDescricao("Icone de telefone", new List<string> { "contato", "ligacao" }, "Outline");
        _analisadorMock.Setup(a => a.DescreverIconeAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var cache = new IconDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<IconDescricaoCache>>());
        var icon = new AssetVisual { Caminho = iconPath };

        var result = await cache.ObterDescricaoAsync(icon, CancellationToken.None);

        result.DescricaoGeral.Should().Be("Icone de telefone");
        _analisadorMock.Verify(a => a.DescreverIconeAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        var sidecarPath = iconPath + ".desc.json";
        File.Exists(sidecarPath).Should().BeTrue();
        var written = await File.ReadAllTextAsync(sidecarPath);
        written.Should().Contain("telefone");
    }

    [Fact]
    public async Task ObterDescricaoAsync_WhenSidecarWriteFails_ShouldReturnDescriptionAnyway()
    {
        var readOnlyDir = Path.Combine(_tempDir, "readonly_test");
        Directory.CreateDirectory(readOnlyDir);
        var iconPath = Path.Combine(readOnlyDir, "test.png");
        await File.WriteAllBytesAsync(iconPath, new byte[] { 0x89, 0x50 });

        var expected = new IconDescricao("Desc", new List<string> { "teste" }, "Flat");
        _analisadorMock.Setup(a => a.DescreverIconeAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var cache = new IconDescricaoCache(_analisadorMock.Object, Mock.Of<ILogger<IconDescricaoCache>>());
        var icon = new AssetVisual { Caminho = iconPath };

        var result = await cache.ObterDescricaoAsync(icon, CancellationToken.None);

        result.Should().NotBeNull();
        result.DescricaoGeral.Should().Be("Desc");
    }
}
