using System.Text.Json;
using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.Pipeline;

public class OrquestradorStep : IPipelineStep
{
    private readonly ILogger<OrquestradorStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly HistoricoChat _historico;

    public string Nome => "orquestrador";

    public OrquestradorStep(
        ILogger<OrquestradorStep> logger,
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
        await NotificarProgresso(context.OnProgresso, "🧠 Analisando seu pedido...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var orquestrador = _agenteLoader.ObterPorPapel("orquestrador");
        if (orquestrador == null)
        {
            _logger.LogError("Agente orquestrador nao encontrado");
            context.Resultado.RespostaFinal = "Erro interno: orquestrador nao configurado.";
            return new PipelineStepResult { DeveContinuar = false };
        }

        var historicoMensagens = _historico.ObterHistorico(context.ChatId);
        var historicoTexto = string.Join("\n", historicoMensagens.Select(m => $"{m.Role}: {m.Content}"));
        var agentesProducao = _agenteLoader.ListarAgentesProducao();
        var listaAgentes = string.Join(", ", agentesProducao.Select(a => $"{a.Nome}: {a.Descricao}"));

        context.ListaAgentesProducao = listaAgentes;
        context.AgentesProducao = agentesProducao;

        var instrucoesOrquestrador = $"Historico do chat:\n{historicoTexto}\n\nMensagem atual: {context.Mensagem}\n\nAgentes de producao disponiveis: {listaAgentes}";

        var respostaOrquestrador = await _openRouter.ChamarAgenteAsync(
            context.ChatId,
            orquestrador.Persona,
            orquestrador.ModeloAlvo,
            instrucoesOrquestrador,
            Nome,
            temperature: 0.3,
            ct: context.CancellationToken);

        context.RespostaOrquestrador = respostaOrquestrador;

        var jsonOrquestrador = OpenRouterService.ExtrairJson(respostaOrquestrador);
        string acao = "fora_contexto";
        string? briefing = null;
        string? respostaDireta = null;

        if (string.IsNullOrEmpty(jsonOrquestrador))
        {
            _logger.LogWarning("Orquestrador nao retornou JSON valido, usando fallback para direta");
            acao = "direta";
            respostaDireta = respostaOrquestrador;
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonOrquestrador);
                acao = doc.RootElement.GetProperty("acao").GetString() ?? "fora_contexto";

                if (doc.RootElement.TryGetProperty("briefing", out var briefingEl))
                    briefing = briefingEl.GetString();
                if (doc.RootElement.TryGetProperty("resposta", out var respostaEl))
                    respostaDireta = respostaEl.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao parsear JSON do orquestrador, usando fallback");
                acao = "direta";
                respostaDireta = respostaOrquestrador;
            }
        }

        context.Rota = acao;
        context.Briefing = briefing;
        context.RespostaDireta = respostaDireta;
        context.Resultado.Rota = acao;

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
