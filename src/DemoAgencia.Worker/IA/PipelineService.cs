using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA.Pipeline;

namespace DemoAgencia.Worker.IA;

public class PipelineService
{
    private readonly ILogger<PipelineService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OrquestradorStep _orquestradorStep;
    private readonly DiretaStep _diretaStep;
    private readonly EstrategistaPlanejadorStep _estrategistaPlanejadorStep;
    private readonly ProducaoStep _producaoStep;
    private readonly QualidadeStep _qualidadeStep;
    private readonly AprovadorStep _aprovadorStep;
    private readonly FormatadorStep _formatadorStep;

    public PipelineService(
        ILogger<PipelineService> logger,
        ILoggerFactory loggerFactory,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader,
        HistoricoChat historico,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _orquestradorStep = new OrquestradorStep(
            loggerFactory.CreateLogger<OrquestradorStep>(),
            openRouter, agenteLoader, historico);
        _diretaStep = new DiretaStep(
            loggerFactory.CreateLogger<DiretaStep>(),
            openRouter, agenteLoader, historico);
        _estrategistaPlanejadorStep = new EstrategistaPlanejadorStep(
            loggerFactory.CreateLogger<EstrategistaPlanejadorStep>(),
            openRouter, agenteLoader);
        _producaoStep = new ProducaoStep(
            loggerFactory.CreateLogger<ProducaoStep>(),
            openRouter);
        _qualidadeStep = new QualidadeStep(
            loggerFactory.CreateLogger<QualidadeStep>(),
            openRouter, agenteLoader);
        _aprovadorStep = new AprovadorStep(
            loggerFactory.CreateLogger<AprovadorStep>(),
            openRouter, agenteLoader);
        _formatadorStep = new FormatadorStep(
            loggerFactory.CreateLogger<FormatadorStep>(),
            openRouter, agenteLoader, historico);
    }

    public virtual async Task<ResultadoPipeline> ExecutarAsync(
        long chatId,
        string mensagem,
        Func<string, Task>? onProgresso = null,
        CancellationToken ct = default)
    {
        var context = new PipelineContext
        {
            ChatId = chatId,
            Mensagem = mensagem,
            OnProgresso = onProgresso,
            CancellationToken = ct,
            MaxRefacoes = _configuration.GetValue<int>("Pipeline:MaxRefacoes", 2)
        };

        var orqResult = await _orquestradorStep.ExecutarAsync(context);
        if (!orqResult.DeveContinuar)
            return context.Resultado;

        if (context.Rota == "fora_contexto")
        {
            context.Resultado.RespostaFinal = _configuration["Pipeline:MensagemForaContexto"]
                ?? "Opa, *suspiro*, infelizmente nao consigo te responder sobre isso.";
            return context.Resultado;
        }

        if (context.Rota == "direta")
        {
            await _diretaStep.ExecutarAsync(context);
            return context.Resultado;
        }

        if (string.IsNullOrEmpty(context.Briefing))
        {
            _logger.LogWarning("Briefing vazio do orquestrador, usando mensagem original");
            context.Briefing = mensagem;
        }

        return await ExecutarPipelineAsync(context);
    }

    private async Task<ResultadoPipeline> ExecutarPipelineAsync(PipelineContext context)
    {
        while (context.Refacoes <= context.MaxRefacoes)
        {
            if (context.Refacoes == 0)
            {
                var planResult = await _estrategistaPlanejadorStep.ExecutarAsync(context);
                if (!planResult.DeveContinuar)
                    return context.Resultado;
            }

            if (context.AgenteProducao == null || string.IsNullOrEmpty(context.InstrucoesProducao))
            {
                _logger.LogError("Agente de producao ou instrucoes vazias");
                context.Resultado.RespostaFinal = _configuration["Pipeline:MensagemFalhaPipeline"]
                    ?? "Nao consegui produzir um resultado. Tente reformular.";
                return context.Resultado;
            }

            await _producaoStep.ExecutarAsync(context);

            var qualResult = await _qualidadeStep.ExecutarAsync(context);
            if (!qualResult.DeveContinuar)
                return context.Resultado;

            if (qualResult.DeveRefazer)
            {
                context.Refacoes++;
                _logger.LogInformation("Qualidade reprovou, refacao {Refacao}/{Max}", context.Refacoes, context.MaxRefacoes);
                context.FeedbackAnterior = context.FeedbackQualidade;
                context.InstrucoesProducao = $"Instrucoes originais:\n{context.InstrucoesOriginais}\n\nFeedback para correcao:\n{context.FeedbackQualidade}";
                await NotificarProgresso(context.OnProgresso, $"🔁 Refinando ({context.Refacoes}/{context.MaxRefacoes})...");
                continue;
            }

            var apvResult = await _aprovadorStep.ExecutarAsync(context);
            if (!apvResult.DeveContinuar)
                return context.Resultado;

            if (apvResult.DeveRefazer)
            {
                context.Refacoes++;
                _logger.LogInformation("Estrategista reprovou, refacao {Refacao}/{Max}", context.Refacoes, context.MaxRefacoes);
                context.FeedbackAnterior = context.ObservacoesEstrategista;
                context.InstrucoesProducao = $"Instrucoes originais:\n{context.InstrucoesOriginais}\n\nFeedback para correcao:\n{context.ObservacoesEstrategista}";
                await NotificarProgresso(context.OnProgresso, $"🔁 Refinando ({context.Refacoes}/{context.MaxRefacoes})...");
                continue;
            }

            if (context.AprovadoEstrategista)
            {
                context.AprovadoFinal = true;
                break;
            }

            break;
        }

        if (!context.AprovadoFinal)
        {
            _logger.LogWarning("Pipeline excedeu maximo de refacoes");
            context.Resultado.RespostaFinal = _configuration["Pipeline:MensagemFalhaPipeline"]
                ?? "Nao consegui produzir um resultado com a qualidade esperada. Pode reformular o pedido?";
            return context.Resultado;
        }

        await _formatadorStep.ExecutarAsync(context);
        return context.Resultado;
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
