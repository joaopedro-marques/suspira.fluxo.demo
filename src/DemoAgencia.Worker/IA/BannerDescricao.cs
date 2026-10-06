using System.Text.Json;

namespace DemoAgencia.Worker.IA;

public record BannerDescricao(
    string DescricaoGeral = "",
    string Composicao = "",
    List<string> PaletaDominante = null!,
    string Estilo = "",
    string Mood = "",
    string TextoPresente = "")
{
    public BannerDescricao() : this("", "", new List<string>(), "", "", "") { }

    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string response, out BannerDescricao? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var json = JsonHelper.ExtrairJson(response);
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            result = JsonSerializer.Deserialize<BannerDescricao>(json, SnakeOptions);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public string ToPromptSection()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(DescricaoGeral)) parts.Add($"Description: {DescricaoGeral}");
        if (!string.IsNullOrEmpty(Composicao)) parts.Add($"Composition: {Composicao}");
        if (PaletaDominante.Count > 0) parts.Add($"Dominant colors: {string.Join(", ", PaletaDominante)}");
        if (!string.IsNullOrEmpty(Estilo)) parts.Add($"Style: {Estilo}");
        if (!string.IsNullOrEmpty(Mood)) parts.Add($"Mood: {Mood}");
        if (!string.IsNullOrEmpty(TextoPresente)) parts.Add($"Text present: {TextoPresente}");
        return string.Join("\n", parts);
    }
}
