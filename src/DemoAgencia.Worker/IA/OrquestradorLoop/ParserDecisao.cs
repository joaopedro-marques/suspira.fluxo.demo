using System.Text.Json;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public record DecisaoOrquestrador(
    string Acao,
    string? Agente = null,
    string? Briefing = null,
    string? Ferramenta = null,
    JsonElement? Parametros = null,
    string? Resposta = null,
    string? Entregavel = null,
    string? Cliente = null,
    IReadOnlyList<string>? ArtefatosIds = null);

public static class ParserDecisao
{
    public static DecisaoOrquestrador? TentarExtrair(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var json = ExtrairJson(texto);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var acao = root.TryGetProperty("acao", out var acaoEl) ? acaoEl.GetString() : null;
            if (string.IsNullOrEmpty(acao))
                return null;

            var agente = root.TryGetProperty("agente", out var agEl) ? agEl.GetString() : null;
            var briefing = root.TryGetProperty("briefing", out var brEl) ? brEl.GetString() : null;
            var ferramenta = root.TryGetProperty("ferramenta", out var feEl) ? feEl.GetString() : null;
            var resposta = root.TryGetProperty("resposta", out var rpEl) ? rpEl.GetString() : null;
            var entregavel = root.TryGetProperty("entregavel", out var enEl) ? enEl.GetString() : null;
            var cliente = root.TryGetProperty("cliente", out var clEl) ? clEl.GetString() : null;

            var artefatosIds = new List<string>();
            if (root.TryGetProperty("artefatos", out var artEl) && artEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in artEl.EnumerateArray())
                {
                    var id = item.GetString();
                    if (!string.IsNullOrEmpty(id))
                        artefatosIds.Add(id);
                }
            }

            JsonElement? parametros = null;
            if (root.TryGetProperty("parametros", out var paramEl))
            {
                parametros = paramEl.Clone();
            }

            return new DecisaoOrquestrador(acao, agente, briefing, ferramenta, parametros, resposta, entregavel, cliente, artefatosIds);
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtrairJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var primeiro = texto.IndexOf('{');
        var ultimo = texto.LastIndexOf('}');

        if (primeiro >= 0 && ultimo > primeiro)
        {
            return texto.Substring(primeiro, ultimo - primeiro + 1);
        }

        return null;
    }
}
