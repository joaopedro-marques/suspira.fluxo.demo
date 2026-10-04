using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class AnexarAssetFerramentaTests : IDisposable
{
    private readonly string _tempDir;

    public AnexarAssetFerramentaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ExecutarAsync_WithValidAssetId_ShouldAttachToResult()
    {
        var assetPath = Path.Combine(_tempDir, "header.png");
        var assetBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(assetPath, assetBytes);

        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "asset_1", Cliente = "acme", Tipo = TipoAsset.Header, Nome = "principal", Caminho = assetPath }
        }.AsReadOnly());

        var ferramenta = new AnexarAssetFerramenta(Mock.Of<ILogger<AnexarAssetFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"asset_id\": \"asset_1\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("anexado com sucesso");
        context.Resultado.AssetsAnexados.Should().HaveCount(1);
        context.Resultado.AssetsAnexados[0].Bytes.Should().BeEquivalentTo(assetBytes);
        context.Resultado.AssetsAnexados[0].Legenda.Should().Contain("Header");
    }

    [Fact]
    public async Task ExecutarAsync_WithInvalidAssetId_ShouldReturnFailure()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>().AsReadOnly());

        var ferramenta = new AnexarAssetFerramenta(Mock.Of<ILogger<AnexarAssetFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"asset_id\": \"asset_999\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("nao encontrado");
        context.Resultado.AssetsAnexados.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_WithoutAssetId_ShouldReturnFailure()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        var ferramenta = new AnexarAssetFerramenta(Mock.Of<ILogger<AnexarAssetFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutCliente_ShouldReturnFailure()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        var ferramenta = new AnexarAssetFerramenta(Mock.Of<ILogger<AnexarAssetFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"asset_id\": \"asset_1\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("cliente nao definido");
    }
}
