using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class ListarAssetsFerramentaTests
{
    [Fact]
    public async Task ExecutarAsync_WithAssets_ShouldReturnCompactCatalog()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "asset_1", Tipo = TipoAsset.Header, Nome = "principal", Descricao = "Header azul" },
            new() { Id = "asset_2", Tipo = TipoAsset.Footer, Nome = "padrao", Descricao = "Footer cinza" }
        }.AsReadOnly());

        var ferramenta = new ListarAssetsFerramenta(Mock.Of<ILogger<ListarAssetsFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"cliente\": \"acme\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("asset_1");
        resultado.Should().Contain("Header");
        resultado.Should().Contain("principal");
        resultado.Should().Contain("Header azul");
        resultado.Should().Contain("asset_2");
        resultado.Should().Contain("Footer");
    }

    [Fact]
    public async Task ExecutarAsync_WithNoAssets_ShouldReturnMessage()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(x => x.ListarAssets("unknown")).Returns(new List<AssetVisual>().AsReadOnly());

        var ferramenta = new ListarAssetsFerramenta(Mock.Of<ILogger<ListarAssetsFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"cliente\": \"unknown\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Nenhum asset");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutClienteParam_ShouldUseContextCliente()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "asset_1", Tipo = TipoAsset.Logo, Nome = "logo", Descricao = "Logo circular" }
        }.AsReadOnly());

        var ferramenta = new ListarAssetsFerramenta(Mock.Of<ILogger<ListarAssetsFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("asset_1");
    }

    [Fact]
    public async Task ExecutarAsync_WithNoCliente_ShouldReturnFailure()
    {
        var referenciasMock = new Mock<IReferenciasCliente>();
        var ferramenta = new ListarAssetsFerramenta(Mock.Of<ILogger<ListarAssetsFerramenta>>(), referenciasMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
    }
}
