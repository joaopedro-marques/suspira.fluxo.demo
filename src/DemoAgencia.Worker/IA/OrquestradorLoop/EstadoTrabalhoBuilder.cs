namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public static class EstadoTrabalhoBuilder
{
    public static string Build(LoopContext context)
    {
        var linhas = new List<string>
        {
            "## Estado do trabalho",
            "",
            $"Turno {context.Turnos}/{context.MaxTurnos}",
            ""
        };

        if (context.Artefatos.Count == 0)
        {
            linhas.Add("Artefatos: nenhum");
        }
        else
        {
            linhas.Add($"Artefatos ({context.Artefatos.Count}):");
            foreach (var art in context.Artefatos)
            {
                linhas.Add($"- [{art.Id}] {art.Tipo} por {art.Agente} ({art.Tamanho} chars): {art.Resumo}");
            }
        }

        linhas.Add("");

        if (context.ImagensDeck.Count == 0)
        {
            linhas.Add("Imagens geradas: nenhuma");
        }
        else
        {
            linhas.Add($"Imagens geradas ({context.ImagensDeck.Count}):");
            foreach (var img in context.ImagensDeck)
            {
                var legendaDisplay = !string.IsNullOrEmpty(img.Legenda) ? $" — \"{img.Legenda}\"" : "";
                linhas.Add($"- [{img.Id}] {img.Papel}{legendaDisplay}");
            }
        }

        if (context.PlanoDeck.Count > 0)
        {
            linhas.Add("");
            var papeisGerados = new HashSet<string>(context.ImagensDeck.Select(i => i.Papel));
            var count = context.PlanoDeck.Count(p => papeisGerados.Contains(p));
            linhas.Add($"Plano do deck: {count}/{context.PlanoDeck.Count} papeis gerados [{string.Join(", ", context.PlanoDeck)}]");
        }

        linhas.Add("");

        if (context.Resultado.EtapasExecutadas.Count > 0)
        {
            linhas.Add("Etapas executadas:");
            foreach (var etapa in context.Resultado.EtapasExecutadas)
            {
                linhas.Add($"- {etapa}");
            }
        }
        else
        {
            linhas.Add("Etapas executadas: nenhuma");
        }

        return string.Join("\n", linhas);
    }
}
