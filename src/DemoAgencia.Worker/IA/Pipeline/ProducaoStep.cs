namespace DemoAgencia.Worker.IA.Pipeline;

public class ProducaoStep : IPipelineStep
{
    private readonly ILogger<ProducaoStep> _logger;
    private readonly OpenRouterService _openRouter;

    public string Nome => "producao";

    public ProducaoStep(
        ILogger<ProducaoStep> logger,
        OpenRouterService openRouter)
    {
        _logger = logger;
        _openRouter = openRouter;
    }

    public async Task<PipelineStepResult> ExecutarAsync(PipelineContext context)
    {
        var agente = context.AgenteProducao!;
        var etapaNome = $"producao_{agente.Nome}";

        await NotificarProgresso(context.OnProgresso, $"✍️ Produzindo com {agente.Nome}...");
        context.Resultado.EtapasExecutadas.Add(etapaNome);

        var isEditorImagens = agente.Tipo.Equals("imagem", StringComparison.OrdinalIgnoreCase);

        if (isEditorImagens)
        {
            var promptOtimizado = await _openRouter.ChamarAgenteAsync(
                context.ChatId,
                agente.Persona,
                agente.ModeloAlvo,
                context.InstrucoesProducao!,
                $"{etapaNome}_enriquecimento",
                temperature: agente.Temperatura,
                ct: context.CancellationToken);

            await NotificarProgresso(context.OnProgresso, "🎨 Gerando imagem...");
            var imagemBytes = await _openRouter.GerarImagemAsync(context.ChatId, promptOtimizado, context.CancellationToken);

            if (imagemBytes != null)
            {
                context.Resultado.Imagem = imagemBytes;
                context.Resultado.LegendaImagem = context.Mensagem;
                context.OutputProducao = $"Imagem gerada com sucesso. Prompt otimizado: {promptOtimizado}";
            }
            else
            {
                context.OutputProducao = "Falha ao gerar imagem.";
            }
        }
        else
        {
            context.OutputProducao = await _openRouter.ChamarAgenteAsync(
                context.ChatId,
                agente.Persona,
                agente.ModeloAlvo,
                context.InstrucoesProducao!,
                etapaNome,
                temperature: agente.Temperatura,
                ct: context.CancellationToken);
        }

        return new PipelineStepResult { DeveContinuar = true };
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
