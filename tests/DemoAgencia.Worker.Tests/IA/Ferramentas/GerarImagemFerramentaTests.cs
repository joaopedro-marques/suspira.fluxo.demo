using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class GerarImagemFerramentaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IReferenciasCliente> _referenciasMock;
    private readonly Mock<IAnalisadorImagem> _analisadorMock;

    public GerarImagemFerramentaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _referenciasMock = new Mock<IReferenciasCliente>();
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _referenciasMock.Setup(x => x.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>().AsReadOnly());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private GerarImagemFerramenta CriarFerramenta(Mock<IGeradorImagem> openRouterMock, LoopOptions? options = null)
        => new(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object, _referenciasMock.Object, _analisadorMock.Object, options ?? new LoopOptions());

    [Fact]
    public async Task ExecutarAsync_ShouldCallOpenRouterAndReturnDescription()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        var imagemBytes = new byte[] { 1, 2, 3 };
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(imagemBytes, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("gerada");
        context.Resultado.Imagens.Should().HaveCount(1);
        context.Resultado.Imagens[0].Bytes.Should().BeEquivalentTo(imagemBytes);
    }

    [Fact]
    public async Task ExecutarAsync_WhenNoLegenda_ShouldUseNullCaption()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        context.Resultado.Imagens[0].Legenda.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WithLegenda_ShouldUseItAsCaption()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\", \"legenda\": \"Foto de um gato\"}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        context.Resultado.Imagens[0].Legenda.Should().Be("Foto de um gato");
    }

    [Fact]
    public async Task ExecutarAsync_WhenImageFails_ShouldReturnFailureMessageWithErrorReason()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(null, "HTTP 404 - endpoint not found"));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("HTTP 404");
        context.Resultado.Imagens.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_MultiplasChamadas_ShouldAdicionarTodasImagens()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        var img1 = new byte[] { 1, 2, 3 };
        var img2 = new byte[] { 4, 5, 6 };

        openRouterMock
            .SetupSequence(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(img1, null))
            .ReturnsAsync(new ResultadoImagem(img2, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };

        var p1 = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"gato\", \"legenda\": \"Gato\"}");
        var p2 = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"cachorro\", \"legenda\": \"Cachorro\"}");

        await ferramenta.ExecutarAsync(context, p1, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, p2, CancellationToken.None);

        context.Resultado.Imagens.Should().HaveCount(2);
        context.Resultado.Imagens[0].Legenda.Should().Be("Gato");
        context.Resultado.Imagens[1].Legenda.Should().Be("Cachorro");
    }

    [Fact]
    public async Task ExecutarAsync_ResultShouldNotContainPrompt()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var promptLongo = "A professional marketing photograph of a modern minimalist workspace";
        var parametros = JsonSerializer.Deserialize<JsonElement>($"{{\"prompt\": \"{promptLongo}\"}}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().NotContain(promptLongo);
        resultado.Should().Contain("gerada");
    }

    [Fact]
    public async Task ExecutarAsync_WithAssets_ShouldEnrichPromptWithDescriptions()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var assetPath = Path.Combine(_tempDir, "header.png");
        await File.WriteAllBytesAsync(assetPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        _referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "asset_1", Cliente = "acme", Tipo = TipoAsset.Header, Nome = "principal", Caminho = assetPath }
        }.AsReadOnly());

        _analisadorMock.Setup(x => x.DescreverImagemAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Header azul com logo branco");

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"post para instagram\", \"assets\": [\"asset_1\"]}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        openRouterMock.Verify(x => x.GerarImagemAsync(
            It.IsAny<long>(),
            It.Is<string>(p => p.Contains("Visual identity references") && p.Contains("Header azul")),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ExecutarAsync_ShouldRegisterInImagensDeckWithSequentialId()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\", \"papel\": \"capa\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("img_1");
        resultado.Should().Contain("capa");
        context.ImagensDeck.Should().HaveCount(1);
        context.ImagensDeck[0].Id.Should().Be("img_1");
        context.ImagensDeck[0].Papel.Should().Be("capa");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutPapel_ShouldDefaultPapelToId()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        context.ImagensDeck[0].Papel.Should().Be("img_1");
    }

    [Fact]
    public async Task ExecutarAsync_MultipleCalls_ShouldAssignSequentialIds()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .SetupSequence(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 2 }, null))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 3 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };

        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"capa\"}")!, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p2\", \"papel\": \"slide_1\"}")!, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p3\", \"papel\": \"slide_2\"}")!, CancellationToken.None);

        context.ImagensDeck.Should().HaveCount(3);
        context.ImagensDeck[0].Id.Should().Be("img_1");
        context.ImagensDeck[1].Id.Should().Be("img_2");
        context.ImagensDeck[2].Id.Should().Be("img_3");
        context.ImagensDeck.Select(i => i.Papel).Should().ContainInOrder("capa", "slide_1", "slide_2");
    }

    [Fact]
    public async Task ExecutarAsync_ResultShouldContainDeckSummary()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\", \"papel\": \"capa\", \"legenda\": \"Capa\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("img_1");
        resultado.Should().Contain("capa");
        resultado.Should().Contain("Deck: 1");
        context.ImagensDeck[0].Legenda.Should().Be("Capa");
        context.ImagensDeck[0].PromptResumo.Should().Contain("um gato");
    }

    [Fact]
    public async Task ExecutarAsync_WhenPapelAlreadyExistsWithoutSubstituir_ShouldBlock()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };

        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"capa\"}")!, CancellationToken.None);

        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p2\", \"papel\": \"capa\"}")!, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("capa");
        resultado.Should().Contain("img_1");
        resultado.Should().Contain("substituir");
        context.ImagensDeck.Should().HaveCount(1);
        context.Resultado.Imagens.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecutarAsync_WhenPapelNotInPlan_ShouldBlock()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        context.PlanoDeck.AddRange(["capa", "slide_1"]);

        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"slide_inexistente\"}")!, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("plano");
        context.ImagensDeck.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_WhenPlanNotSet_ShouldAllowWithoutPapelGuard()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };

        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"qualquer\"}")!, CancellationToken.None);

        resultado.Should().Contain("gerada");
        context.ImagensDeck.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecutarAsync_WithSubstituir_ShouldReplaceAtSamePosition()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        var img1Bytes = new byte[] { 1, 2, 3 };
        var newBytes = new byte[] { 9, 9, 9 };
        var img2Bytes = new byte[] { 4, 5, 6 };

        openRouterMock
            .SetupSequence(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(img1Bytes, null))
            .ReturnsAsync(new ResultadoImagem(img2Bytes, null))
            .ReturnsAsync(new ResultadoImagem(newBytes, null));

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };

        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"capa\"}")!, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p2\", \"papel\": \"slide_1\"}")!, CancellationToken.None);
        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p3\", \"papel\": \"capa\", \"substituir\": \"img_1\"}")!, CancellationToken.None);

        resultado.Should().Contain("img_1");
        resultado.Should().Contain("substituida");
        context.ImagensDeck.Should().HaveCount(2);
        context.ImagensDeck[0].Id.Should().Be("img_1");
        context.ImagensDeck[0].Papel.Should().Be("capa");
        context.Resultado.Imagens.Should().HaveCount(2);
        context.Resultado.Imagens[0].Bytes.Should().BeEquivalentTo(newBytes);
        context.Resultado.Imagens[1].Bytes.Should().BeEquivalentTo(img2Bytes);
    }

    [Fact]
    public async Task ExecutarAsync_WithSubstituir_NonExistentId_ShouldBlock()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123 };
        context.ImagensDeck.Add(new ItemDeckImagem("img_1", "capa", null, "prompt"));

        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"capa\", \"substituir\": \"img_99\"}")!, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("img_99");
        context.Resultado.Imagens.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_WhenMaxImagesReached_ShouldBlock()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var options = new LoopOptions { MaxImagensPorDeck = 2 };
        var ferramenta = CriarFerramenta(openRouterMock, options);
        var context = new LoopContext { ChatId = 123 };

        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"a\"}")!, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p2\", \"papel\": \"b\"}")!, CancellationToken.None);
        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p3\", \"papel\": \"c\"}")!, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("limite");
        context.ImagensDeck.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecutarAsync_WithSubstituir_ShouldNotCountTowardsMax()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var options = new LoopOptions { MaxImagensPorDeck = 2 };
        var ferramenta = CriarFerramenta(openRouterMock, options);
        var context = new LoopContext { ChatId = 123 };

        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p1\", \"papel\": \"a\"}")!, CancellationToken.None);
        await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p2\", \"papel\": \"b\"}")!, CancellationToken.None);
        var resultado = await ferramenta.ExecutarAsync(context, JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"p3\", \"papel\": \"a\", \"substituir\": \"img_1\"}")!, CancellationToken.None);

        resultado.Should().Contain("substituida");
        context.ImagensDeck.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecutarAsync_WithoutAssetsParam_ShouldAutoInjectBrandAssets()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var logoPath = Path.Combine(_tempDir, "logo.png");
        var iconPath = Path.Combine(_tempDir, "icon.png");
        await File.WriteAllBytesAsync(logoPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        await File.WriteAllBytesAsync(iconPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        _referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "logo", Cliente = "acme", Tipo = TipoAsset.Logo, Nome = "principal", Caminho = logoPath },
            new() { Id = "icon", Cliente = "acme", Tipo = TipoAsset.Icon, Nome = "marca", Caminho = iconPath },
            new() { Id = "header", Cliente = "acme", Tipo = TipoAsset.Header, Nome = "email", Caminho = logoPath }
        }.AsReadOnly());

        _analisadorMock.Setup(x => x.DescreverImagemAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Logo vermelha circular");

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"post para instagram\"}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        openRouterMock.Verify(x => x.GerarImagemAsync(
            It.IsAny<long>(),
            It.Is<string>(p => p.Contains("Visual identity references") && p.Contains("Logo") && p.Contains("Icon") && !p.Contains("Header")),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ExecutarAsync_WithAssetsParam_ShouldNotAutoInjectBrand()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        _referenciasMock.Setup(x => x.ListarAssets("acme")).Returns(new List<AssetVisual>
        {
            new() { Id = "logo", Cliente = "acme", Tipo = TipoAsset.Logo, Nome = "principal", Descricao = "Logo" }
        }.AsReadOnly());

        var ferramenta = CriarFerramenta(openRouterMock);
        var context = new LoopContext { ChatId = 123, Cliente = "acme" };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"post\", \"assets\": []}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        openRouterMock.Verify(x => x.GerarImagemAsync(
            It.IsAny<long>(),
            It.Is<string>(p => !p.Contains("Visual identity references")),
            It.IsAny<CancellationToken>()));
    }
}
