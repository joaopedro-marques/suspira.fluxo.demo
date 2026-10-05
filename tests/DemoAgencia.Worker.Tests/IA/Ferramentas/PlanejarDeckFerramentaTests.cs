using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class PlanejarDeckFerramentaTests
{
    [Fact]
    public async Task ExecutarAsync_ShouldSetPlanoDeck()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"papeis\": [\"capa\", \"slide_1\", \"slide_2\"]}");

        await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        context.PlanoDeck.Should().HaveCount(3);
        context.PlanoDeck.Should().ContainInOrder("capa", "slide_1", "slide_2");
    }

    [Fact]
    public async Task ExecutarAsync_ResultShouldContainPlanSummary()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"papeis\": [\"capa\", \"slide_1\"]}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Plano");
        resultado.Should().Contain("2");
        resultado.Should().Contain("capa");
        resultado.Should().Contain("slide_1");
    }

    [Fact]
    public async Task ExecutarAsync_WhenDeckHasImages_ShouldRefuseToClear()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        context.ImagensDeck.Add(new ItemDeckImagem("img_1", "capa", "legenda", "prompt"));
        context.Resultado.Imagens.Add(new ImagemGerada(new byte[] { 1 }, "legenda"));

        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"papeis\": [\"nova_capa\", \"slide_1\"]}");
        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
        resultado.Should().Contain("img_1");
        resultado.Should().Contain("substituir");
        context.PlanoDeck.Should().BeEmpty();
        context.ImagensDeck.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecutarAsync_WithEmptyDeck_ShouldSetPlanAndClearNothing()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"papeis\": [\"capa\", \"slide_1\"]}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Plano");
        resultado.Should().Contain("2");
        context.PlanoDeck.Should().ContainInOrder("capa", "slide_1");
        context.ImagensDeck.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_EmptyPapeis_ShouldReturnError()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        var parametros = JsonSerializer.Deserialize<JsonElement>("{\"papeis\": []}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
        context.PlanoDeck.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_NoPapeisParam_ShouldReturnError()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        var context = new LoopContext();
        var parametros = JsonSerializer.Deserialize<JsonElement>("{}");

        var resultado = await ferramenta.ExecutarAsync(context, parametros, CancellationToken.None);

        resultado.Should().Contain("Falha");
    }

    [Fact]
    public void Nome_ShouldBePlanejarDeck()
    {
        var ferramenta = new PlanejarDeckFerramenta(Mock.Of<ILogger<PlanejarDeckFerramenta>>());
        ferramenta.Nome.Should().Be("planejar_deck");
    }
}
