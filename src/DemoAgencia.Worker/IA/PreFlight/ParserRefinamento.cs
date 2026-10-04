using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.PreFlight;

public class ResultadoRefinamento
{
    public bool PrecisaEsclarecimento { get; init; }
    public List<string> Perguntas { get; init; } = new();
    public string? PedidoRefinado { get; init; }
    public string? Cliente { get; init; }
    public bool Simples { get; init; }
}

public static class ParserRefinamento
{
    public static ResultadoRefinamento? TentarExtrair(string texto)
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

            var precisaEsclarecimento = root.TryGetProperty("precisa_esclarecimento", out var peEl)
                                        && peEl.GetBoolean();

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

            var pedidoRefinado = root.TryGetProperty("pedido_refinado", out var prEl) ? prEl.GetString() : null;
            var cliente = root.TryGetProperty("cliente", out var clEl) ? clEl.GetString() : null;
            var simples = root.TryGetProperty("simples", out var sEl) && sEl.GetBoolean();

            return new ResultadoRefinamento
            {
                PrecisaEsclarecimento = precisaEsclarecimento,
                Perguntas = perguntas,
                PedidoRefinado = pedidoRefinado,
                Cliente = cliente,
                Simples = simples
            };
        }
        catch
        {
            return null;
        }
    }
}
