using System.Text.Json;
using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class AprovadorStep : IPipelineStep
{
    private readonly ILogger<AprovadorStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;

    public string Nome => "estrategista_aprovador";

    public AprovadorStep(
        ILogger<AprovadorStep> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
    }

    public async Task<PipelineStepResult> ExecutarAsync(PipelineContext context)
    {
        await NotificarProgresso(context.OnProgresso, "✅ Aprovando...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var estrategistaAprovador = _agenteLoader.ObterPorPapel("estrategista");
        if (estrategistaAprovador == null)
        {
            _logger.LogError("Agente estrategista nao encontrado para aprovacao");
            context.Resultado.RespostaFinal = "Erro interno: estrategista nao configurado.";
            return new PipelineStepResult { DeveContinuar = false };
        }

        var instrucoesAprovacao = $"Briefing original:\n{context.Briefing}\n\nOutput do agente:\n{context.OutputProducao}\n\nVeredito da qualidade: {context.VereditoQualidade}\nFeedback: {context.FeedbackQualidade}";

        var aprovacaoEstrategista = await _openRouter.ChamarAgenteAsync(
            context.ChatId,
            estrategistaAprovador.Persona,
            estrategistaAprovador.ModeloAlvo,
            instrucoesAprovacao,
            Nome,
            temperature: 0.3,
            ct: context.CancellationToken);

        var jsonAprovacao = OpenRouterService.ExtrairJson(aprovacaoEstrategista);
        bool aprovado = true;
        string? observacoes = null;

        if (!string.IsNullOrEmpty(jsonAprovacao))
        {
            try
            {
                using var docAprovacao = JsonDocument.Parse(jsonAprovacao);
                aprovado = docAprovacao.RootElement.GetProperty("aprovado").GetBoolean();
                if (docAprovacao.RootElement.TryGetProperty("observacoes", out var obsEl))
                    observacoes = obsEl.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao parsear aprovacao do estrategista, assumindo aprovado");
                aprovado = true;
            }
        }

        context.AprovadoEstrategista = aprovado;
        context.ObservacoesEstrategista = observacoes;

        bool deveRefazer = !aprovado && context.Refacoes < context.MaxRefacoes;

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
