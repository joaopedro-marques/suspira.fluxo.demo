using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines;

public class PipelineRunner
{
    private readonly ILogger<PipelineRunner> _logger;

    public PipelineRunner(ILogger<PipelineRunner> logger)
    {
        _logger = logger;
    }

    public virtual async Task<ResultadoPipeline> ExecutarAsync(
        PipelineContext ctx,
        IReadOnlyList<IPipelineStep> steps,
        int maxRefacoesQa,
        CancellationToken ct)
    {
        var stepIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var idx = 0; idx < steps.Count; idx++)
            stepIndices[steps[idx].Nome] = idx;

        var snapshots = new Dictionary<int, (int imagens, int assets)>();
        var i = 0;

        while (i < steps.Count)
        {
            var step = steps[i];
            snapshots[i] = (ctx.Resultado.Imagens.Count, ctx.Resultado.AssetsAnexados.Count);

            ctx.Resultado.EtapasExecutadas.Add(step.Nome);
            ctx = await step.ExecutarAsync(ctx, ct);

            if (step.Nome.Equals("qa", StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.QaAprovado)
                {
                    ctx.Resultado.QaAprovado = true;
                    return ctx.Resultado;
                }

                ctx.Refacoes++;
                if (ctx.Refacoes > maxRefacoesQa)
                {
                    _logger.LogWarning("QA reprovou; limite de {Max} refacoes atingido. Feedback: {Feedback}",
                        maxRefacoesQa, ctx.QaFeedback);
                    ctx.Resultado.QaAprovado = false;
                    ctx.Resultado.QaFeedbackFinal = ctx.QaFeedback;
                    return ctx.Resultado;
                }

                var alvo = ctx.QaStepAlvo ?? "copy";
                if (stepIndices.TryGetValue(alvo, out var targetIndex))
                {
                    var snapshot = snapshots[targetIndex];
                    Truncate(ctx.Resultado.Imagens, snapshot.imagens);
                    Truncate(ctx.Resultado.AssetsAnexados, snapshot.assets);
                    i = targetIndex;
                    _logger.LogInformation("QA reprovou. Refazendo a partir de {Step} (refacao {Refacao}/{Max})",
                        alvo, ctx.Refacoes, maxRefacoesQa);
                    continue;
                }

                _logger.LogWarning("Step alvo '{Alvo}' nao encontrado. Usando copy.", alvo);
                if (stepIndices.TryGetValue("copy", out var copyIndex))
                {
                    var snapshot = snapshots[copyIndex];
                    Truncate(ctx.Resultado.Imagens, snapshot.imagens);
                    Truncate(ctx.Resultado.AssetsAnexados, snapshot.assets);
                    i = copyIndex;
                    continue;
                }

                ctx.Resultado.QaAprovado = false;
                ctx.Resultado.QaFeedbackFinal = ctx.QaFeedback ?? "QA reprovou.";
                return ctx.Resultado;
            }

            i++;
        }

        return ctx.Resultado;
    }

    private static void Truncate<T>(List<T> list, int maxCount)
    {
        while (list.Count > maxCount)
            list.RemoveAt(list.Count - 1);
    }
}
