using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepEstrategiaEmail : IPipelineStep
{
    public string Nome => "estrategia";

    private readonly IReferenciasCliente _referencias;

    public StepEstrategiaEmail(IReferenciasCliente referencias)
    {
        _referencias = referencias;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var cliente = context.Cliente;
        var etapa = context.Brief.EtapaJornada;

        if (string.IsNullOrEmpty(cliente) || string.IsNullOrEmpty(etapa))
        {
            context.Estrategia = null;
            return Task.FromResult(context);
        }

        var estrategia = _referencias.ObterEstrategia(cliente);
        if (estrategia == null)
        {
            context.Estrategia = null;
            return Task.FromResult(context);
        }

        if (!estrategia.Fases.TryGetValue(etapa, out var faseDados))
        {
            context.Estrategia = null;
            return Task.FromResult(context);
        }

        context.Estrategia = new EstrategiaEmail
        {
            Fase = etapa,
            FaseDados = faseDados,
            MapaEmocional = new List<EtapaEmocional>(estrategia.MapaEmocional),
            Satisfacoes = new List<CategoriaSatisfacao>(estrategia.Satisfacoes),
            Insatisfacoes = new List<CategoriaSatisfacao>(estrategia.Insatisfacoes),
            SubJornada = context.Brief.SubJornada
        };

        return Task.FromResult(context);
    }
}
