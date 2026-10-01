using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class DiretaStep : IPipelineStep
{
    private readonly ILogger<DiretaStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly HistoricoChat _historico;

    public string Nome => "formatador";

    public DiretaStep(
        ILogger<DiretaStep> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader,
        HistoricoChat historico)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _historico = historico;
    }

    public async Task<PipelineStepResult> ExecutarAsync(PipelineContext context)
    {
        await NotificarProgresso(context.OnProgresso, "📤 Formatando resposta...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var formatador = _agenteLoader.ObterPorPapel("formatacao");
        if (formatador != null && !string.IsNullOrEmpty(context.RespostaDireta))
        {
            var respostaFormatada = await _openRouter.ChamarAgenteAsync(
                context.ChatId,
                formatador.Persona,
                formatador.ModeloAlvo,
                $"Pedido original: {context.Mensagem}\n\nResposta para formatar:\n{context.RespostaDireta}",
                Nome,
                ct: context.CancellationToken);

            context.Resultado.RespostaFinal = respostaFormatada;
        }
        else
        {
            context.Resultado.RespostaFinal = context.RespostaDireta ?? context.RespostaOrquestrador ?? "";
        }

        _historico.AdicionarMensagem(context.ChatId, "user", context.Mensagem);
        _historico.AdicionarMensagem(context.ChatId, "assistant", context.Resultado.RespostaFinal);

        return new PipelineStepResult { DeveContinuar = false };
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
