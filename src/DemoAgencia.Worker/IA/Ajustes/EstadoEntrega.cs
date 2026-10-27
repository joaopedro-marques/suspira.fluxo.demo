using DemoAgencia.Worker.IA.Pipelines;

namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Entrega vigente de um chat. Guarda o contexto final da pipeline para permitir
/// re-execucao pontual a partir de um step especifico.
/// </summary>
public sealed class EstadoEntrega
{
    public required PipelineContext Contexto { get; set; }

    public int Versao { get; set; } = 1;

    public int RodadasAjuste { get; set; }

    public bool Aprovada { get; set; }

    public FaseAjuste Fase { get; set; } = FaseAjuste.AposEntrega;

    public PlanoAjuste? Plano { get; set; }
}
