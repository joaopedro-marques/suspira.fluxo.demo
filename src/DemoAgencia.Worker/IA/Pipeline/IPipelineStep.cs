namespace DemoAgencia.Worker.IA.Pipeline;

public interface IPipelineStep
{
    string Nome { get; }
    Task<PipelineStepResult> ExecutarAsync(PipelineContext context);
}

public class PipelineStepResult
{
    public bool DeveContinuar { get; init; } = true;
    public bool DeveRefazer { get; init; } = false;
}
