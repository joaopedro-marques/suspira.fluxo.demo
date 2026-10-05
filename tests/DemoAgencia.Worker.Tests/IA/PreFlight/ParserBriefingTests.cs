using DemoAgencia.Worker.IA.PreFlight;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.PreFlight;

public class ParserBriefingTests
{
    [Fact]
    public void TentarExtrair_WithAllFields_ShouldParseAll()
    {
        var texto = """{"briefing": "Criar post Instagram para Acme", "imagens_necessarias": ["logo principal"]}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Criar post Instagram para Acme");
        resultado.ImagensNecessarias.Should().BeEquivalentTo("logo principal");
    }

    [Fact]
    public void TentarExtrair_WithoutOptionalFields_ShouldReturnEmptyLists()
    {
        var texto = """{"briefing": "Post simples"}""";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Post simples");
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

    [Fact]
    public void TentarExtrair_WithTruncatedJsonNoClosingBrace_ShouldRepairAndExtract()
    {
        var texto = "{\"briefing\": \"Post para Instagram\", \"assets_reservados\": [\"a_1\"]";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Post para Instagram");
    }

    [Fact]
    public void TentarExtrair_WithTruncatedJsonMidString_ShouldRepairAndExtract()
    {
        var texto = "{\"briefing\": \"Post para Insta";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Be("Post para Insta");
    }

    [Fact]
    public void TentarExtrair_WithUnescapedQuotesInBriefing_ShouldFallbackAndExtract()
    {
        var texto = "{\"briefing\": \"Use o titulo \"Campanha de Verao\" e o slogan \"Refresque-se\"\", \"assets_reservados\": [\"a_1\"]}";

        var resultado = ParserBriefing.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Briefing.Should().Contain("Campanha de Verao");
        resultado!.Briefing.Should().Contain("Refresque-se");
    }

    [Fact]
    public void TentarExtrairComDiagnostico_WithValidJson_ShouldReturnJsonPuroPath()
    {
        var texto = """{"briefing": "Briefing valido"}""";

        var parse = ParserBriefing.TentarExtrairComDiagnostico(texto);

        parse.Resultado.Should().NotBeNull();
        parse.MotivoFalha.Should().BeNull();
        parse.CaminhoParse.Should().Be("json_puro");
    }

    [Fact]
    public void TentarExtrairComDiagnostico_WithTruncatedJson_ShouldReturnReparadoPath()
    {
        var texto = "{\"briefing\": \"Post para Instagram\"";

        var parse = ParserBriefing.TentarExtrairComDiagnostico(texto);

        parse.Resultado.Should().NotBeNull();
        parse.CaminhoParse.Should().Be("reparado");
    }

    [Fact]
    public void TentarExtrairComDiagnostico_WithInvalidText_ShouldReturnMotivo()
    {
        var texto = "sem json nem briefing";

        var parse = ParserBriefing.TentarExtrairComDiagnostico(texto);

        parse.Resultado.Should().BeNull();
        parse.MotivoFalha.Should().NotBeNullOrEmpty();
    }
}
