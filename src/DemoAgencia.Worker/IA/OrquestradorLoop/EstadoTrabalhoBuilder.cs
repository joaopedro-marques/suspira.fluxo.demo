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
