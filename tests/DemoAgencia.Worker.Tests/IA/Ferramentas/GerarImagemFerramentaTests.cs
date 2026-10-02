using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class GerarImagemFerramentaTests
{
    [Fact]
    public async Task ExecutarAsync_ShouldCallOpenRouterAndReturnDescription()
    {
        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());
        var anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
        anonimizadorMock.Setup(x => x.Anonimizar(It.IsAny<string>())).Returns<string>(s => s);
        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object,
            anonimizadorMock.Object);

        var openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object,
            Mock.Of<IHttpClientFactory>());

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
        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());
        var anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
        anonimizadorMock.Setup(x => x.Anonimizar(It.IsAny<string>())).Returns<string>(s => s);
        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object,
            anonimizadorMock.Object);

        var openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object,
            Mock.Of<IHttpClientFactory>());

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
