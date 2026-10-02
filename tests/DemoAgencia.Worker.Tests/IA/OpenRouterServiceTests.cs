using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class OpenRouterServiceTests
{
    [Theory]
    [InlineData("{\"acao\": \"pipeline\"}", "{\"acao\": \"pipeline\"}")]
    [InlineData("Aqui esta o JSON: {\"acao\": \"direta\"} ok", "{\"acao\": \"direta\"}")]
    [InlineData("{\"a\": 1, \"b\": {\"c\": 2}}", "{\"a\": 1, \"b\": {\"c\": 2}}")]
    [InlineData("Texto sem JSON", null)]
    [InlineData("", null)]
    [InlineData("{}", "{}")]
    [InlineData("{invalido}", "{invalido}")]
    public void ExtrairJson_ShouldExtractJsonFromText(string input, string? expected)
    {
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtrairJson_WithNestedJson_ShouldExtractOuter()
    {
        var input = "{\"outer\": {\"inner\": \"value\"}}";
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be(input);
    }

    [Fact]
    public void ExtrairJson_WithMarkdownCodeBlock_ShouldExtractJson()
    {
        var input = "```json\n{\"acao\": \"pipeline\"}\n```";
        var result = ParserDecisao.ExtrairJson(input);
        result.Should().Be("{\"acao\": \"pipeline\"}");
    }
}
