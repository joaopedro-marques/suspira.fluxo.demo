using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipeline;

public class EstrategistaPlanejadorStep : IPipelineStep
{
    private readonly ILogger<EstrategistaPlanejadorStep> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly ReferenciaClienteLoader _referenciaLoader;
    private readonly HistoricoChat _historico;

    public string Nome => "estrategista_planejador";

    public EstrategistaPlanejadorStep(
        ILogger<EstrategistaPlanejadorStep> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader,
        ReferenciaClienteLoader referenciaLoader,
        HistoricoChat historico)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _referenciaLoader = referenciaLoader;
        _historico = historico;
    }

    public async Task<PipelineStepResult> ExecutarAsync(PipelineContext context)
    {
        await NotificarProgresso(context.OnProgresso, "📋 Planejando execucao...");
        context.Resultado.EtapasExecutadas.Add(Nome);

        var estrategista = _agenteLoader.ObterPorPapel("estrategista");
        if (estrategista == null)
        {
            _logger.LogError("Agente estrategista nao encontrado");
            context.Resultado.RespostaFinal = "Erro interno: estrategista nao configurado.";
            return new PipelineStepResult { DeveContinuar = false };
        }

        var promptBase = $"Briefing do orquestrador:\n{context.Briefing}\n\nAgentes disponiveis: {context.ListaAgentesProducao}";

        if (!string.IsNullOrEmpty(context.Cliente))
        {
            var referencias = _referenciaLoader.ObterReferenciasTexto(context.Cliente);
            if (!string.IsNullOrEmpty(referencias))
            {
                context.ReferenciasCliente = referencias;
                promptBase += $"\n\nReferencias do cliente {context.Cliente}:\n{referencias}";
            }

            var imagens = _referenciaLoader.ListarImagens(context.Cliente) ?? Array.Empty<string>();
            foreach (var imagemPath in imagens)
            {
                try
                {
                    if (File.Exists(imagemPath))
                    {
                        var imagemBytes = await File.ReadAllBytesAsync(imagemPath, context.CancellationToken);
                        var descricao = await _openRouter.AnalisarImagemAsync(
                            context.ChatId,
                            imagemBytes,
                            $"Contexto: {context.Briefing}",
                            _historico,
                            context.CancellationToken);
                        if (!string.IsNullOrEmpty(descricao))
                        {
                            promptBase += $"\n\nImagem {Path.GetFileName(imagemPath)}: {descricao}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro ao analisar imagem {Imagem}", imagemPath);
                }
            }
        }

        var planoEstrategista = await _openRouter.ChamarAgenteAsync(
            context.ChatId,
            estrategista.Persona,
            estrategista.ModeloAlvo,
            promptBase,
            Nome,
            temperature: estrategista.Temperatura,
            ct: context.CancellationToken);

        var jsonPlano = OpenRouterService.ExtrairJson(planoEstrategista);
        if (!string.IsNullOrEmpty(jsonPlano))
        {
            try
            {
                using var docPlano = JsonDocument.Parse(jsonPlano);
                var nomeAgente = docPlano.RootElement.GetProperty("agente").GetString();
                context.InstrucoesOriginais = docPlano.RootElement.GetProperty("instrucoes").GetString();
                context.InstrucoesProducao = context.InstrucoesOriginais;

                if (docPlano.RootElement.TryGetProperty("criterios_qa", out var criteriosEl) &&
                    criteriosEl.ValueKind == JsonValueKind.Array)
                {
                    var criterios = new List<string>();
                    foreach (var item in criteriosEl.EnumerateArray())
                    {
                        var criterio = item.GetString();
                        if (!string.IsNullOrEmpty(criterio))
                            criterios.Add(criterio);
                    }
                    context.CriteriosQa = criterios.AsReadOnly();
                }

                context.AgenteProducao = _agenteLoader.ObterPorNome(nomeAgente ?? "");
                if (context.AgenteProducao == null)
                {
                    _logger.LogWarning("Agente {Agente} nao encontrado, usando primeiro de producao", nomeAgente);
                    context.AgenteProducao = context.AgentesProducao.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao parsear plano do estrategista");
                context.AgenteProducao = context.AgentesProducao.FirstOrDefault();
                context.InstrucoesOriginais = context.Briefing;
                context.InstrucoesProducao = context.Briefing;
            }
        }
        else
        {
            context.AgenteProducao = context.AgentesProducao.FirstOrDefault();
            context.InstrucoesOriginais = context.Briefing;
            context.InstrucoesProducao = context.Briefing;
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
