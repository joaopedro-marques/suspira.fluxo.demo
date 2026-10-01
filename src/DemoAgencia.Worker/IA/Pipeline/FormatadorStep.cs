using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class FormatadorStep : IPipelineStep
{
    private readonly ILogger<FormatadorStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly HistoricoChat _historico;

    public string Nome => "formatador";

    public FormatadorStep(
        ILogger<FormatadorStep> logger,
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
        await NotificarProgresso(context.OnProgresso, "📤 Formatando resposta final...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var formatadorFinal = _agenteLoader.ObterPorPapel("formatacao");
        if (formatadorFinal != null && !string.IsNullOrEmpty(context.OutputProducao))
        {
            var respostaFormatada = await _openRouter.ChamarAgenteAsync(
                context.ChatId,
                formatadorFinal.Persona,
                formatadorFinal.ModeloAlvo,
                $"Pedido original do usuario: {context.Mensagem}\n\nOutput aprovado para formatar:\n{context.OutputProducao}",
                Nome,
                ct: context.CancellationToken);

            context.Resultado.RespostaFinal = respostaFormatada;
        }
        else
        {
            context.Resultado.RespostaFinal = context.OutputProducao ?? "";
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
