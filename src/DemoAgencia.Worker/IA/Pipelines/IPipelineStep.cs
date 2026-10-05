namespace DemoAgencia.Worker.IA.Pipelines;

public interface IPipelineStep
{
    string Nome { get; }
    Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct);
}
