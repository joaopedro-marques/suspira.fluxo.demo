using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.PreFlight;

public class ResultadoBriefing
{
    public string Briefing { get; init; } = string.Empty;
    public List<string> AssetsReservados { get; init; } = new();
    public List<string> ImagensNecessarias { get; init; } = new();
}

public static class ParserBriefing
{
    public static ResultadoBriefing? TentarExtrair(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var json = ParserDecisao.ExtrairJson(texto);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var briefing = root.TryGetProperty("briefing", out var brEl) ? brEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(briefing))
                return null;

            var assetsReservados = ExtrairListaStrings(root, "assets_reservados");
            var imagensNecessarias = ExtrairListaStrings(root, "imagens_necessarias");

            return new ResultadoBriefing
            {
                Briefing = briefing!,
                AssetsReservados = assetsReservados,
                ImagensNecessarias = imagensNecessarias
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ExtrairListaStrings(JsonElement root, string campo)
    {
        var lista = new List<string>();
        if (root.TryGetProperty(campo, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var valor = item.GetString();
                if (!string.IsNullOrEmpty(valor))
                    lista.Add(valor);
            }
        }
        return lista;
    }
}
