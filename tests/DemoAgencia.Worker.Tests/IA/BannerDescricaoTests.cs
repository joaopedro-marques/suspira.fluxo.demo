using System.Text.Json;
using DemoAgencia.Worker.IA;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class BannerDescricaoTests
{
    [Fact]
    public void BannerDescricao_ShouldSerializeToJson()
    {
        var desc = new BannerDescricao(
            "Banner com ilustracao flat de casa",
            "Imagem centralizada, espaco negativo nas laterais",
            new List<string> { "#006b40", "#F48421", "#F1ECE6" },
            "Ilustracao flat vetorial",
            "Acolhedor e profissional",
            "Agende sua visita tecnica");

        var json = JsonSerializer.Serialize(desc, descricaoOptions);

        json.Should().Contain("descricao_geral");
        json.Should().Contain("composicao");
        json.Should().Contain("paleta_dominante");
        json.Should().Contain("estilo");
        json.Should().Contain("mood");
        json.Should().Contain("texto_presente");
        json.Should().Contain("#006b40");
    }

    [Fact]
    public void BannerDescricao_ShouldDeserializeFromJson()
    {
        var json = """
        {
            "descricao_geral": "Banner flat",
            "composicao": "Centralizado",
            "paleta_dominante": ["#006b40", "#F48421"],
            "estilo": "Flat vetorial",
            "mood": "Acolhedor",
            "texto_presente": "Agende visita"
        }
        """;

        var desc = JsonSerializer.Deserialize<BannerDescricao>(json, descricaoOptions);

        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Banner flat");
        desc.PaletaDominante.Should().HaveCount(2);
        desc.PaletaDominante[0].Should().Be("#006b40");
    }

    [Fact]
    public void BannerDescricao_ShouldDeserializeWithMissingFields()
    {
        var json = """{"descricao_geral": "Basico"}""";

        var desc = JsonSerializer.Deserialize<BannerDescricao>(json, descricaoOptions);

        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Basico");
        desc.Composicao.Should().Be("");
        desc.PaletaDominante.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_ShouldExtractFromMarkdownCodeBlock()
    {
        var response = "Aqui esta:\n```json\n{\"descricao_geral\": \"Banner com casa\", \"paleta_dominante\": [\"#006b40\"]}\n```";

        var ok = BannerDescricao.TryParse(response, out var desc);

        ok.Should().BeTrue();
        desc.Should().NotBeNull();
        desc!.DescricaoGeral.Should().Be("Banner com casa");
        desc.PaletaDominante.Should().Contain("#006b40");
    }

    [Fact]
    public void TryParse_ShouldReturnFalseOnInvalidJson()
    {
        var ok = BannerDescricao.TryParse("texto sem json", out var desc);

        ok.Should().BeFalse();
        desc.Should().BeNull();
    }

    private static readonly JsonSerializerOptions descricaoOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
