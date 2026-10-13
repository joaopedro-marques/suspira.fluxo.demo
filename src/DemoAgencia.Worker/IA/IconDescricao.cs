using System.Text.Json;

namespace DemoAgencia.Worker.IA;

public record IconDescricao(
    string DescricaoGeral = "",
    List<string> PalavrasChave = null!,
    string Estilo = "",
    string CorPredominante = "")
{
    public IconDescricao() : this("", new List<string>(), "", "") { }

    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string response, out IconDescricao? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var json = JsonHelper.ExtrairJson(response);
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            result = JsonSerializer.Deserialize<IconDescricao>(json, SnakeOptions);
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
        if (PalavrasChave.Count > 0) parts.Add($"Keywords: {string.Join(", ", PalavrasChave)}");
        if (!string.IsNullOrEmpty(Estilo)) parts.Add($"Style: {Estilo}");
        if (!string.IsNullOrEmpty(CorPredominante)) parts.Add($"Color: {CorPredominante}");
        return string.Join("\n", parts);
    }
}
