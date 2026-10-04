using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class GerarImagemFerramentaTests
{
    [Fact]
    public async Task ExecutarAsync_ShouldCallOpenRouterAndReturnDescription()
    {
        var openRouterMock = new Mock<IGeradorImagem>();

        var imagemBytes = new byte[] { 1, 2, 3 };
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(imagemBytes, null));

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Imagem gerada");
        context.Resultado.Imagens.Should().HaveCount(1);
        context.Resultado.Imagens[0].Bytes.Should().BeEquivalentTo(imagemBytes);
    }

    [Fact]
    public async Task ExecutarAsync_WhenNoLegenda_ShouldUseEmptyCaption()
    {
        var openRouterMock = new Mock<IGeradorImagem>();
        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImagem(new byte[] { 1 }, null));

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
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

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
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

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
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

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
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

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var promptLongo = "A professional marketing photograph of a modern minimalist workspace";
        var parametros = JsonSerializer.Deserialize<JsonElement>($"{{\"prompt\": \"{promptLongo}\"}}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().NotContain(promptLongo);
        resultado.Should().Contain("Imagem gerada");
    }
}
