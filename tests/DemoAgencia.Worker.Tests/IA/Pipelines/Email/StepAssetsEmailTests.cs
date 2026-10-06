using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepAssetsEmailTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly StepAssetsEmail _step;

    public StepAssetsEmailTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _refsMock = new Mock<IReferenciasCliente>();
        _step = new StepAssetsEmail(_refsMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ExecutarAsync_WithAssetReferences_ShouldPackageMatchingAssets()
    {
        var logoPath = Path.Combine(_tempDir, "MRV_logoMRVCO.png");
        var instaPath = Path.Combine(_tempDir, "MRV_instagram-2.png");
        await File.WriteAllBytesAsync(logoPath, new byte[] { 1 });
        await File.WriteAllBytesAsync(instaPath, new byte[] { 2 });

        var assets = new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Logo, Nome = "logoMRVCO", Caminho = logoPath },
            new() { Cliente = "mrv", Tipo = TipoAsset.Outro, Nome = "instagram-2", Caminho = instaPath }
        };
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(assets);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = """<img src="assets/logoMRVCO.png"><img src="assets/instagram-2.png">"""
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Resultado.AssetsAnexados.Should().HaveCount(2);
        context.Resultado.AssetsAnexados.Should().ContainSingle(a => a.Legenda == "logoMRVCO.png");
        context.Resultado.AssetsAnexados.Should().ContainSingle(a => a.Legenda == "instagram-2.png");
    }

    [Fact]
    public async Task ExecutarAsync_WithDedup_ShouldNotAddTwice()
    {
        var logoPath = Path.Combine(_tempDir, "MRV_logoMRVCO.png");
        await File.WriteAllBytesAsync(logoPath, new byte[] { 1 });

        var assets = new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Logo, Nome = "logoMRVCO", Caminho = logoPath }
        };
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(assets);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = """<img src="assets/logoMRVCO.png"><img src="assets/logoMRVCO.png">"""
        };
        context.Resultado.AssetsAnexados.Add(new ImagemGerada(new byte[] { 99 }, "logoMRVCO.png"));

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Resultado.AssetsAnexados.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecutarAsync_WithUnmatchedReference_ShouldSkipSilently()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>());

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = """<img src="assets/nonexistent.png">"""
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Resultado.AssetsAnexados.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_IgnoresNonAssetsPaths()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = """<img src="imagens/gerada_1.png"><img src="https://example.com/img.png">"""
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Resultado.AssetsAnexados.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_WithoutHtml_ShouldNotThrow()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = null
        };

        var act = () => _step.ExecutarAsync(context, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecutarAsync_WithoutCliente_ShouldNotThrow()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = null,
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Html = "<img src=\"assets/test.png\">"
        };

        var act = () => _step.ExecutarAsync(context, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
