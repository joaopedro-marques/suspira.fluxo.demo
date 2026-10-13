using System.Text.Json;
using DemoAgencia.Worker.IA;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class IconDescricaoTests
{
    [Fact]
    public void IconDescricao_ShouldSerializeToJson()
    {
        var desc = new IconDescricao(
            "Icone de casa em linha simples",
            new List<string> { "moradia", "imovel", "lar" },
            "Flat line");

        var json = JsonSerializer.Serialize(desc, SnakeOptions);

        json.Should().Contain("descricao_geral");
        json.Should().Contain("palavras_chave");
        json.Should().Contain("estilo");
        json.Should().Contain("moradia");
    }

    [Fact]
    public void IconDescricao_ShouldDeserializeFromJson()
    {
        var json = """
        {
            "descricao_geral": "Icone de telefone",
            "palavras_chave": ["contato", "ligacao"],
            "estilo": "Flat"
        }
        """;

        var desc = JsonSerializer.Deserialize<IconDescricao>(json, SnakeOptions);

        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Icone de telefone");
        desc.PalavrasChave.Should().HaveCount(2);
        desc.PalavrasChave[0].Should().Be("contato");
    }

    [Fact]
    public void IconDescricao_ShouldDeserializeWithMissingFields()
    {
        var json = """{"descricao_geral": "Basico"}""";

        var desc = JsonSerializer.Deserialize<IconDescricao>(json, SnakeOptions);

        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Basico");
        desc.PalavrasChave.Should().BeEmpty();
        desc.Estilo.Should().Be("");
    }

    [Fact]
    public void TryParse_ShouldExtractFromMarkdownCodeBlock()
    {
        var response = "Resultado:\n```json\n{\"descricao_geral\": \"Icone de casa\", \"palavras_chave\": [\"moradia\"]}\n```";

        var ok = IconDescricao.TryParse(response, out var desc);

        ok.Should().BeTrue();
        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Icone de casa");
        desc.PalavrasChave.Should().Contain("moradia");
    }

    [Fact]
    public void TryParse_ShouldReturnFalseOnInvalidJson()
    {
        var ok = IconDescricao.TryParse("texto sem json", out var desc);

        ok.Should().BeFalse();
        desc.Should().BeNull();
    }

    [Fact]
    public void ToPromptSection_ShouldFormatCorrectly()
    {
        var desc = new IconDescricao(
            "Icone de casa",
            new List<string> { "moradia", "imovel" },
            "Flat line");

        var section = desc.ToPromptSection();

        section.Should().Contain("Description: Icone de casa");
        section.Should().Contain("Keywords: moradia, imovel");
        section.Should().Contain("Style: Flat line");
    }

    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
