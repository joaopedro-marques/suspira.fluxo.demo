using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class ParserOutputAgenteTests
{
    [Fact]
    public void Extrair_WithValidJsonAllFields_ShouldParseAll()
    {
        var texto = "{\"entregavel\": \"Copy final\", \"notas\": \"Contexto interno\", \"resumo\": \"Copy de 1 paragrafo\"}";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be("Copy final");
        output.Notas.Should().Be("Contexto interno");
        output.Resumo.Should().Be("Copy de 1 paragrafo");
    }

    [Fact]
    public void Extrair_WithOnlyEntregavel_ShouldReturnOnlyEntregavel()
    {
        var texto = "{\"entregavel\": \"Copy final\"}";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be("Copy final");
        output.Notas.Should().BeNull();
        output.Resumo.Should().BeNull();
    }

    [Fact]
    public void Extrair_WithJsonInsideMarkdown_ShouldExtract()
    {
        var texto = "Segue o resultado:\n```json\n{\"entregavel\": \"HTML aqui\", \"resumo\": \"Email\"}\n```\nPronto.";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be("HTML aqui");
        output.Resumo.Should().Be("Email");
    }

    [Fact]
    public void Extrair_WithRawText_ShouldFallbackEntregavelAsWholeText()
    {
        var texto = "Copy completa sem JSON, texto livre do agente.";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be(texto);
        output.Notas.Should().BeNull();
        output.Resumo.Should().BeNull();
    }

    [Fact]
    public void Extrair_WithEmptyString_ShouldReturnEmptyEntregavel()
    {
        var output = ParserOutputAgente.Extrair("");

        output.Entregavel.Should().Be(string.Empty);
        output.Notas.Should().BeNull();
        output.Resumo.Should().BeNull();
    }

    [Fact]
    public void Extrair_WithNull_ShouldReturnNullEntregavel()
    {
        var output = ParserOutputAgente.Extrair(null!);

        output.Entregavel.Should().BeNull();
        output.Notas.Should().BeNull();
        output.Resumo.Should().BeNull();
    }

    [Fact]
    public void Extrair_WithInvalidJson_ShouldFallbackToRawText()
    {
        var texto = "{entregavel: invalid}";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be(texto);
        output.Notas.Should().BeNull();
    }

    [Fact]
    public void Extrair_WithMultilineHtmlInEntregavel_ShouldPreserveContent()
    {
        var htmlInJson = "<!DOCTYPE html>\\n<html>\\n<body>\\n<h1>Olá</h1>\\n</body>\\n</html>";
        var expectedHtml = "<!DOCTYPE html>\n<html>\n<body>\n<h1>Olá</h1>\n</body>\n</html>";
        var texto = $"{{\"entregavel\": \"{htmlInJson}\", \"notas\": \"HTML completo\"}}";

        var output = ParserOutputAgente.Extrair(texto);

        output.Entregavel.Should().Be(expectedHtml);
        output.Notas.Should().Be("HTML completo");
    }
}
