using DemoAgencia.Worker.IA.PreFlight;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.PreFlight;

public class ParserBriefingTests
{
    [Fact]
    public void TentarExtrair_WithAllFields_ShouldParseAll()
    {
        var texto = """{"briefing": "Criar post Instagram para Acme", "assets_reservados": ["asset_1", "asset_3"], "imagens_necessarias": ["logo principal"]}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Criar post Instagram para Acme");
        resultado.AssetsReservados.Should().BeEquivalentTo("asset_1", "asset_3");
        resultado.ImagensNecessarias.Should().BeEquivalentTo("logo principal");
    }

    [Fact]
    public void TentarExtrair_WithoutAssetsReservados_ShouldReturnEmptyList()
    {
        var texto = """{"briefing": "Post simples"}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Post simples");
        resultado.AssetsReservados.Should().BeEmpty();
        resultado.ImagensNecessarias.Should().BeEmpty();
    }

    [Fact]
    public void TentarExtrair_WithJsonInMarkdown_ShouldExtract()
    {
        var texto = "Segue o briefing:\n```json\n{\"briefing\": \"HTML para email\"}\n```";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("HTML para email");
    }

    [Fact]
    public void TentarExtrair_WithInvalidJson_ShouldReturnNull()
    {
        var texto = "Texto sem JSON valido";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEmptyString_ShouldReturnNull()
    {
        var resultado = ParserBriefing.TentarExtrair("");

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithoutBriefing_ShouldReturnNull()
    {
        var texto = """{"assets_reservados": ["asset_1"]}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEmptyBriefing_ShouldReturnNull()
    {
        var texto = """{"briefing": ""}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithWhitespaceBriefing_ShouldReturnNull()
    {
        var texto = """{"briefing": "   "}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().BeNull();
    }
}
