using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines;

public class PipelineRunner
{
    private readonly ILogger<PipelineRunner> _logger;

    public PipelineRunner(ILogger<PipelineRunner> logger)
    {
        _logger = logger;
    }

    public virtual Task<ResultadoPipeline> ExecutarAsync(
        PipelineContext ctx,
        IReadOnlyList<IPipelineStep> steps,
        int maxRefacoesQa,
        CancellationToken ct)
    {
        return ExecutarCoreAsync(ctx, steps, maxRefacoesQa, ct, startIndex: 0);
    }

    /// <summary>
    /// Re-executa a pipeline a partir de um step especifico (ajuste pontual pos-entrega).
    /// Retrieval anteriores ao step alvo sao reaproveitados do contexto da entrega.
    /// </summary>
    public virtual Task<ResultadoPipeline> ExecutarAjusteAsync(
        PipelineContext ctx,
        IReadOnlyList<IPipelineStep> steps,
        string stepAlvo,
        int maxRefacoesQa,
        CancellationToken ct)
    {
        var stepIndices = MapearIndices(steps);
        if (!stepIndices.TryGetValue(stepAlvo, out var indiceAlvo))
        {
            _logger.LogWarning("Step alvo '{Alvo}' nao encontrado no ajuste. Usando copy.", stepAlvo);
            indiceAlvo = stepIndices.TryGetValue("copy", out var copyIndex) ? copyIndex : 0;
        }

        if (steps.Count == 0)
        {
            _logger.LogWarning("Ajuste solicitado sem steps configurados.");
            return Task.FromResult(ctx.Resultado);
        }

        PrepararAjuste(ctx, steps[indiceAlvo].Nome);

        _logger.LogInformation("Ajuste pontual: executando a partir de {Step} (indice {Indice})",
            steps[indiceAlvo].Nome, indiceAlvo);

        return ExecutarCoreAsync(ctx, steps, maxRefacoesQa, ct, indiceAlvo);
    }

    private static Dictionary<string, int> MapearIndices(IReadOnlyList<IPipelineStep> steps)
    {
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var idx = 0; idx < steps.Count; idx++)
            indices[steps[idx].Nome] = idx;
        return indices;
    }

    private static void PrepararAjuste(PipelineContext ctx, string stepAlvo)
    {
        ctx.Refacoes = 0;
        ctx.QaAprovado = false;
        ctx.QaFeedback = null;
        ctx.QaStepAlvo = null;

        if (stepAlvo.Equals("hero", StringComparison.OrdinalIgnoreCase))
        {
            // O hero sera refeito: descarta a midia gerada na versao anterior para nao
            // arrastar imagens orfas para o zip da nova entrega.
            ctx.Resultado.Imagens.Clear();
            RemoverBannerAnterior(ctx);
            ctx.HeroSrc = null;
            ctx.BannerSrc = null;
            ctx.PreservarHero = false;
            return;
        }

        // Ajustes de copy/diagramacao/template preservam a imagem da entrega anterior.
        ctx.PreservarHero = true;
    }

    private static void RemoverBannerAnterior(PipelineContext ctx)
    {
        const string prefixo = "assets/";
        if (string.IsNullOrEmpty(ctx.BannerSrc) || !ctx.BannerSrc.StartsWith(prefixo, StringComparison.Ordinal))
            return;

        var legenda = ctx.BannerSrc[prefixo.Length..];
        ctx.Resultado.AssetsAnexados.RemoveAll(a => a.Legenda == legenda);
    }

    private async Task<ResultadoPipeline> ExecutarCoreAsync(
        PipelineContext ctx,
        IReadOnlyList<IPipelineStep> steps,
        int maxRefacoesQa,
        CancellationToken ct,
        int startIndex)
    {
        var stepIndices = MapearIndices(steps);
        var snapshots = new Dictionary<int, (int imagens, int assets)>();
        var i = startIndex;

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
                    if (alvo.Equals("hero", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ctx.PreservarHero)
                        {
                            // Ajuste preservava a midia, mas o QA pediu refacao do hero:
                            // recria a imagem sem manter a anterior no zip.
                            ctx.Resultado.Imagens.Clear();
                            RemoverBannerAnterior(ctx);
                            ctx.HeroSrc = null;
                            ctx.BannerSrc = null;
                        }

                        ctx.PreservarHero = false;
                    }

                    var snapshot = snapshots.TryGetValue(targetIndex, out var salvo)
                        ? salvo
                        : (ctx.Resultado.Imagens.Count, ctx.Resultado.AssetsAnexados.Count);
                    Truncate(ctx.Resultado.Imagens, snapshot.Item1);
                    Truncate(ctx.Resultado.AssetsAnexados, snapshot.Item2);
                    i = targetIndex;
                    _logger.LogInformation("QA reprovou. Refazendo a partir de {Step} (refacao {Refacao}/{Max})",
                        alvo, ctx.Refacoes, maxRefacoesQa);
                    continue;
                }

                _logger.LogWarning("Step alvo '{Alvo}' nao encontrado. Usando copy.", alvo);
                if (stepIndices.TryGetValue("copy", out var copyIndex))
                {
                    var snapshot = snapshots.TryGetValue(copyIndex, out var salvoCopy)
                        ? salvoCopy
                        : (ctx.Resultado.Imagens.Count, ctx.Resultado.AssetsAnexados.Count);
                    Truncate(ctx.Resultado.Imagens, snapshot.Item1);
                    Truncate(ctx.Resultado.AssetsAnexados, snapshot.Item2);
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
