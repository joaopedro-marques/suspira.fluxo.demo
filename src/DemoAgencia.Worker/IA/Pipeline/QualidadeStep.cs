using System.Text.Json;
using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class QualidadeStep : IPipelineStep
{
    private readonly ILogger<QualidadeStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;

    public string Nome => "qualidade";

    public QualidadeStep(
        ILogger<QualidadeStep> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
    }

    public async Task<PipelineStepResult> ExecutarAsync(PipelineContext context)
    {
        await NotificarProgresso(context.OnProgresso, "🔍 Revisando qualidade...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var qualidade = _agenteLoader.ObterPorPapel("qualidade");
        if (qualidade == null)
        {
            _logger.LogError("Agente qualidade nao encontrado");
            context.Resultado.RespostaFinal = "Erro interno: qualidade nao configurada.";
            return new PipelineStepResult { DeveContinuar = false };
        }

        var instrucoesQualidade = $"Instrucoes originais:\n{context.InstrucoesOriginais}\n\nOutput do agente:\n{context.OutputProducao}";

        if (context.CriteriosQa.Count > 0)
        {
            var criteriosTexto = string.Join("\n", context.CriteriosQa.Select((c, i) => $"{i + 1}. {c}"));
            instrucoesQualidade += $"\n\nCriterios objetivos definidos pelo estrategista (valide cada um):\n{criteriosTexto}";
        }

        if (context.Refacoes > 0 && !string.IsNullOrEmpty(context.FeedbackAnterior))
        {
            instrucoesQualidade += $"\n\nFeedback da iteracao anterior: {context.FeedbackAnterior}";
        }

        var vereditoQualidade = await _openRouter.ChamarAgenteAsync(
            context.ChatId,
            qualidade.Persona,
            qualidade.ModeloAlvo,
            instrucoesQualidade,
            Nome,
            temperature: qualidade.Temperatura,
            ct: context.CancellationToken);

        var jsonVeredito = OpenRouterService.ExtrairJson(vereditoQualidade);
        string veredito = "aprovado";
        string? feedback = null;

        if (!string.IsNullOrEmpty(jsonVeredito))
        {
            try
            {
                using var docVeredito = JsonDocument.Parse(jsonVeredito);
                veredito = docVeredito.RootElement.GetProperty("veredito").GetString() ?? "aprovado";
                if (docVeredito.RootElement.TryGetProperty("feedback", out var feedbackEl))
                    feedback = feedbackEl.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao parsear veredito da qualidade, assumindo aprovado");
                veredito = "aprovado";
            }
        }

        context.VereditoQualidade = veredito;
        context.FeedbackQualidade = feedback;

        bool deveRefazer = veredito == "reprovado" && context.Refacoes < context.MaxRefacoes;

        return new PipelineStepResult { DeveContinuar = true, DeveRefazer = deveRefazer };
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
