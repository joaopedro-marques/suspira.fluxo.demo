using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class PlanejarDeckFerramenta : IFerramenta
{
    private readonly ILogger<PlanejarDeckFerramenta> _logger;

    public string Nome => "planejar_deck";
    public string Descricao => "Define o plano de um deck de imagens (papéis de cada slide). Deve ser chamado antes de gerar múltiplas imagens.";

    public PlanejarDeckFerramenta(ILogger<PlanejarDeckFerramenta> logger)
    {
        _logger = logger;
    }

    public Task<string> ExecutarAsync(LoopContext context, JsonElement parametros, CancellationToken ct)
    {
        if (!parametros.TryGetProperty("papeis", out var papeisEl) || papeisEl.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult("Falha: parametro 'papeis' (array de strings) e obrigatorio.");
        }

        var papeis = new List<string>();
        foreach (var item in papeisEl.EnumerateArray())
        {
            var papel = item.GetString();
            if (!string.IsNullOrWhiteSpace(papel))
                papeis.Add(papel);
        }

        if (papeis.Count == 0)
        {
            return Task.FromResult("Falha: 'papeis' deve conter pelo menos um papel.");
        }

        _logger.LogInformation("Planejando deck com {Count} papeis: {Papeis}", papeis.Count, string.Join(", ", papeis));

        context.PlanoDeck.Clear();
        foreach (var papel in papeis)
            context.PlanoDeck.Add(papel);

        context.ImagensDeck.Clear();
        context.Resultado.Imagens.Clear();

        return Task.FromResult($"Plano de deck definido: {papeis.Count} imagens [{string.Join(", ", papeis)}]. Gere cada imagem com o papel correspondente usando gerar_imagem.");
    }
}
