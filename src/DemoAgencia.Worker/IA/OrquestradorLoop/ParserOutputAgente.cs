using System.Text.Json;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public record OutputAgente(string? Entregavel, string? Notas, string? Resumo);

public static class ParserOutputAgente
{
    public static OutputAgente Extrair(string? texto)
    {
        if (texto == null)
            return new OutputAgente(null, null, null);

        if (string.IsNullOrWhiteSpace(texto))
            return new OutputAgente(string.Empty, null, null);

        var json = ParserDecisao.ExtrairJson(texto);
        if (string.IsNullOrEmpty(json))
            return new OutputAgente(texto, null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var entregavel = root.TryGetProperty("entregavel", out var e) ? e.GetString() : null;
            var notas = root.TryGetProperty("notas", out var n) ? n.GetString() : null;
            var resumo = root.TryGetProperty("resumo", out var r) ? r.GetString() : null;
            return new OutputAgente(entregavel, notas, resumo);
        }
        catch
        {
            return new OutputAgente(texto, null, null);
        }
    }
}
