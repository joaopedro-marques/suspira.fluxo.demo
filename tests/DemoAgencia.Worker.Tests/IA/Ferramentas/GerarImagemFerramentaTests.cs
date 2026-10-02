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
            .ReturnsAsync(imagemBytes);

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Imagem gerada");
        context.Resultado.Imagem.Should().BeEquivalentTo(imagemBytes);
    }

    [Fact]
    public async Task ExecutarAsync_WhenImageFails_ShouldReturnFailureMessage()
    {
        var openRouterMock = new Mock<IGeradorImagem>();

        openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var ferramenta = new GerarImagemFerramenta(Mock.Of<ILogger<GerarImagemFerramenta>>(), openRouterMock.Object);
        var context = new LoopContext { ChatId = 123 };
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"prompt\": \"um gato\"}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
        context.Resultado.Imagem.Should().BeNull();
    }
}
