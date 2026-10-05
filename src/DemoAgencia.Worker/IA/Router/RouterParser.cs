using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.Router;

public static class RouterParser
{
    private static readonly HashSet<string> TiposValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "fora_contexto", "conversa", "esclarecimento", "producao"
    };

    private static readonly HashSet<string> CanaisValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "instagram", "landing"
    };

    public static RouterResultado? TentarExtrair(string texto)
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

            var tipo = root.TryGetProperty("tipo", out var tipoEl) ? tipoEl.GetString() : null;
            if (string.IsNullOrEmpty(tipo) || !TiposValidos.Contains(tipo))
                return null;

            var resposta = root.TryGetProperty("resposta", out var resEl) ? resEl.GetString() : null;
            var cliente = root.TryGetProperty("cliente", out var cliEl) ? cliEl.GetString()?.ToLowerInvariant() : null;

            var perguntas = new List<string>();
            if (root.TryGetProperty("perguntas", out var pergEl) && pergEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in pergEl.EnumerateArray())
                {
                    var p = item.GetString();
                    if (!string.IsNullOrEmpty(p))
                        perguntas.Add(p);
                }
            }

            Brief? brief = null;
            if (root.TryGetProperty("brief", out var briefEl) && briefEl.ValueKind == JsonValueKind.Object)
            {
                brief = ExtrairBrief(briefEl);
                if (brief == null)
                    return null;
            }

            if (string.Equals(tipo, "producao", StringComparison.OrdinalIgnoreCase) && brief == null)
                return null;

            return new RouterResultado(tipo, resposta, perguntas, cliente, brief);
        }
        catch
        {
            return null;
        }
    }

    private static Brief? ExtrairBrief(JsonElement el)
    {
        var canal = el.TryGetProperty("canal", out var canalEl) ? canalEl.GetString() : null;
        if (string.IsNullOrEmpty(canal) || !CanaisValidos.Contains(canal))
            return null;

        var objetivo = el.TryGetProperty("objetivo", out var objEl) ? objEl.GetString() : null;
        var publico = el.TryGetProperty("publico", out var pubEl) ? pubEl.GetString() : null;
        var oferta = el.TryGetProperty("oferta", out var ofEl) ? ofEl.GetString() : null;
        var tom = el.TryGetProperty("tom", out var tomEl) ? tomEl.GetString() : null;
        var link = el.TryGetProperty("link", out var linkEl) ? linkEl.GetString() : null;

        var restricoes = new List<string>();
        if (el.TryGetProperty("restricoes", out var restEl) && restEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in restEl.EnumerateArray())
            {
                var r = item.GetString();
                if (!string.IsNullOrEmpty(r))
                    restricoes.Add(r);
            }
        }

        var imagens = new List<ImagemBrief>();
        if (el.TryGetProperty("imagens", out var imgEl) && imgEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in imgEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var papel = item.TryGetProperty("papel", out var papelE) ? papelE.GetString() : null;
                var descricao = item.TryGetProperty("descricao", out var descE) ? descE.GetString() : null;
                if (!string.IsNullOrEmpty(papel) && !string.IsNullOrEmpty(descricao))
                    imagens.Add(new ImagemBrief(papel, descricao));
            }
        }

        return new Brief(canal, objetivo, publico, oferta, tom, link, restricoes, imagens);
    }
}
