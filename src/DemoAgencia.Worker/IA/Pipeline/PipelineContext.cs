using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class PipelineContext
{
    public long ChatId { get; init; }
    public string Mensagem { get; init; } = string.Empty;
    public ResultadoPipeline Resultado { get; } = new();
    public Func<string, Task>? OnProgresso { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public string Rota { get; set; } = string.Empty;
    public string? Briefing { get; set; }
    public string? RespostaDireta { get; set; }
    public string? RespostaOrquestrador { get; set; }

    public AgenteDefinicao? AgenteProducao { get; set; }
    public string? InstrucoesOriginais { get; set; }
    public string? InstrucoesProducao { get; set; }
    public string? OutputProducao { get; set; }
    public string? FeedbackAnterior { get; set; }

    public int Refacoes { get; set; }
    public int MaxRefacoes { get; set; } = 2;
    public bool AprovadoFinal { get; set; }

    public string? VereditoQualidade { get; set; }
    public string? FeedbackQualidade { get; set; }

    public bool AprovadoEstrategista { get; set; }
    public string? ObservacoesEstrategista { get; set; }

    public string ListaAgentesProducao { get; set; } = string.Empty;
    public IReadOnlyCollection<AgenteDefinicao> AgentesProducao { get; set; } = Array.Empty<AgenteDefinicao>();
}
