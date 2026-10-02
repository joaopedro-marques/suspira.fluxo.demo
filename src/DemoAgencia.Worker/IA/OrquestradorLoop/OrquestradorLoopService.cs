using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class OrquestradorLoopService
{
    private readonly ILogger<OrquestradorLoopService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly ReferenciaClienteLoader _referenciaLoader;
    private readonly HistoricoChat _historico;
    private readonly FerramentaRegistry _ferramentaRegistry;

    public OrquestradorLoopService(
        ILogger<OrquestradorLoopService> logger,
        IConfiguration configuration,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader,
        ReferenciaClienteLoader referenciaLoader,
        HistoricoChat historico,
        FerramentaRegistry ferramentaRegistry)
    {
        _logger = logger;
        _configuration = configuration;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _referenciaLoader = referenciaLoader;
        _historico = historico;
        _ferramentaRegistry = ferramentaRegistry;
    }

    public virtual async Task<ResultadoPipeline> ExecutarAsync(
        long chatId,
        string mensagem,
        Func<string, Task>? onProgresso = null,
        CancellationToken ct = default)
    {
        var context = new LoopContext
        {
            ChatId = chatId,
            Mensagem = mensagem,
            OnProgresso = onProgresso,
            CancellationToken = ct,
            MaxTurnos = _configuration.GetValue<int>("Loop:MaxTurnos", 8),
            MaxRefacoesQa = _configuration.GetValue<int>("Loop:MaxRefacoesQa", 2)
        };

        var orquestrador = _agenteLoader.ObterPorPapel("orquestrador");
        if (orquestrador == null)
        {
            _logger.LogError("Orquestrador nao encontrado");
            context.Resultado.RespostaFinal = _configuration["Loop:MensagemFalha"] ?? "Erro interno";
            return context.Resultado;
        }

        var transcript = new List<(string role, string content)>();
        transcript.Add(("system", BuildSystemPrompt(orquestrador)));
        transcript.Add(("user", mensagem));

        var jsonRetry = false;

        while (context.Turnos < context.MaxTurnos)
        {
            context.Turnos++;

            await NotificarProgresso(onProgresso, $"🧠 Turno {context.Turnos}...");

            var transcriptText = string.Join("\n", transcript.Select(t => $"{t.role}: {t.content}"));
            var respostaOrquestrador = await _openRouter.ChamarAgenteAsync(
                chatId,
                orquestrador.Persona,
                orquestrador.ModeloAlvo,
                transcriptText,
                "loop_orquestrador",
                temperature: orquestrador.Temperatura,
                ct: ct);

            var json = OpenRouterService.ExtrairJson(respostaOrquestrador);
            if (string.IsNullOrEmpty(json))
            {
                if (!jsonRetry)
                {
                    jsonRetry = true;
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "Sua resposta nao continha JSON valido. Responda apenas com JSON."));
                    continue;
                }
                context.Resultado.RespostaFinal = _configuration["Loop:MensagemFalha"] ?? "Falha no loop";
                return context.Resultado;
            }

            jsonRetry = false;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch
            {
                if (!jsonRetry)
                {
                    jsonRetry = true;
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "JSON invalido. Responda apenas com JSON valido."));
                    continue;
                }
                context.Resultado.RespostaFinal = _configuration["Loop:MensagemFalha"] ?? "Falha no loop";
                return context.Resultado;
            }

            var acao = doc.RootElement.TryGetProperty("acao", out var acaoEl) ? acaoEl.GetString() : null;

            var acaoHash = ComputeActionHash(json);
            if (acaoHash == context.UltimaAcaoHash && acao != "finalizar")
            {
                transcript.Add(("assistant", respostaOrquestrador));
                transcript.Add(("user", "Voce ja executou essa mesma acao. Tente uma abordagem diferente."));
                continue;
            }
            context.UltimaAcaoHash = acaoHash;

            if (doc.RootElement.TryGetProperty("cliente", out var clienteEl))
            {
                var cliente = clienteEl.GetString();
                if (!string.IsNullOrEmpty(cliente) && string.IsNullOrEmpty(context.Cliente))
                {
                    context.Cliente = cliente;
                    InjectClientReferences(transcript, cliente);
                }
            }

            switch (acao)
            {
                case "fora_contexto":
                    context.Resultado.Rota = "fora_contexto";
                    context.Resultado.RespostaFinal = _configuration["Loop:MensagemForaContexto"]
                        ?? "Fora do contexto";
                    return context.Resultado;

                case "responder_direto":
                    context.Resultado.Rota = "direta";
                    context.Resultado.RespostaFinal = doc.RootElement.TryGetProperty("resposta", out var respEl)
                        ? respEl.GetString() ?? ""
                        : "";
                    return context.Resultado;

                case "chamar_agente":
                    var agenteNome = doc.RootElement.TryGetProperty("agente", out var agenteEl)
                        ? agenteEl.GetString() : null;
                    var briefing = doc.RootElement.TryGetProperty("briefing", out var briefingEl)
                        ? briefingEl.GetString() : null;

                    var agente = _agenteLoader.ObterPorNome(agenteNome ?? "");
                    if (agente == null)
                    {
                        transcript.Add(("assistant", respostaOrquestrador));
                        transcript.Add(("user", $"Agente '{agenteNome}' nao encontrado. Escolha um agente valido."));
                        continue;
                    }

                    await NotificarProgresso(onProgresso, $"✍️ {agente.Nome} trabalhando...");
                    context.Resultado.EtapasExecutadas.Add($"loop_agente_{agente.Nome}");

                    var output = await _openRouter.ChamarAgenteAsync(
                        chatId,
                        agente.Persona,
                        agente.ModeloAlvo,
                        briefing ?? "",
                        $"loop_agente_{agente.Nome}",
                        temperature: agente.Temperatura,
                        ct: ct);

                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", $"Resultado do agente {agente.Nome}: {output}"));
                    break;

                case "chamar_ferramenta":
                    var ferramentaNome = doc.RootElement.TryGetProperty("ferramenta", out var ferrEl)
                        ? ferrEl.GetString() : null;
                    var ferramenta = _ferramentaRegistry.Obter(ferramentaNome ?? "");
                    if (ferramenta == null)
                    {
                        transcript.Add(("assistant", respostaOrquestrador));
                        transcript.Add(("user", $"Ferramenta '{ferramentaNome}' nao encontrada."));
                        continue;
                    }

                    await NotificarProgresso(onProgresso, $"🔧 Executando {ferramenta.Nome}...");

                    var parametros = doc.RootElement.TryGetProperty("parametros", out var paramEl)
                        ? paramEl : JsonDocument.Parse("{}").RootElement;

                    var toolResult = await ferramenta.ExecutarAsync(context, parametros, ct);

                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", $"Resultado da ferramenta {ferramenta.Nome}: {toolResult}"));
                    break;

                case "finalizar":
                    var entregavel = doc.RootElement.TryGetProperty("entregavel", out var entrEl)
                        ? entrEl.GetString() : null;
                    context.Entregavel = entregavel;

                    if (!context.QaExecutado)
                    {
                        await NotificarProgresso(onProgresso, "🔍 Revisando qualidade...");
                        context.Resultado.EtapasExecutadas.Add("loop_qualidade");

                        var qualidade = _agenteLoader.ObterPorPapel("qualidade");
                        if (qualidade == null)
                        {
                            context.QaAprovado = true;
                        }
                        else
                        {
                            var qaPrompt = $"Briefing original: {context.Mensagem}\n\nEntregavel:\n{entregavel}";
                            var qaResult = await _openRouter.ChamarAgenteAsync(
                                chatId,
                                qualidade.Persona,
                                qualidade.ModeloAlvo,
                                qaPrompt,
                                "loop_qualidade",
                                temperature: qualidade.Temperatura,
                                ct: ct);

                            var qaJson = OpenRouterService.ExtrairJson(qaResult);
                            if (!string.IsNullOrEmpty(qaJson))
                            {
                                try
                                {
                                    using var qaDoc = JsonDocument.Parse(qaJson);
                                    context.QaAprovado = qaDoc.RootElement.TryGetProperty("aprovado", out var apEl)
                                        && apEl.GetBoolean();
                                    context.FeedbackQa = qaDoc.RootElement.TryGetProperty("feedback", out var fbEl)
                                        ? fbEl.GetString() : null;
                                }
                                catch
                                {
                                    context.QaAprovado = true;
                                }
                            }
                            else
                            {
                                context.QaAprovado = true;
                            }

                            context.QaExecutado = true;
                        }

                        if (!context.QaAprovado)
                        {
                            context.RefacoesQa++;
                            if (context.RefacoesQa >= context.MaxRefacoesQa)
                            {
                                context.Resultado.RespostaFinal = _configuration["Loop:MensagemFalha"] ?? "Falha no loop";
                                return context.Resultado;
                            }

                            transcript.Add(("assistant", respostaOrquestrador));
                            transcript.Add(("user", $"QA reprovou. Feedback: {context.FeedbackQa}. Melhore o entregavel."));
                            context.QaExecutado = false;
                            continue;
                        }
                    }

                    context.Resultado.RespostaFinal = entregavel ?? "";
                    return context.Resultado;

                default:
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "Acao desconhecida. Use: responder_direto, fora_contexto, chamar_agente, chamar_ferramenta, finalizar."));
                    break;
            }
        }

        context.Resultado.RespostaFinal = _configuration["Loop:MensagemFalha"] ?? "Falha no loop";
        return context.Resultado;
    }

    private string BuildSystemPrompt(AgenteDefinicao orquestrador)
    {
        var agentes = _agenteLoader.ListarAgentesProducao();
        var ferramentas = _ferramentaRegistry.Listar();

        var prompt = orquestrador.Persona;
        prompt += "\n\n## Agentes disponiveis:\n";
        foreach (var a in agentes)
        {
            prompt += $"- {a.Nome}: {a.Descricao}\n";
        }

        if (ferramentas.Any())
        {
            prompt += "\n## Ferramentas disponiveis:\n";
            foreach (var f in ferramentas)
            {
                prompt += $"- {f.Nome}: {f.Descricao}\n";
            }
        }

        prompt += "\n## Protocolo:\nResponda apenas com JSON: {\"acao\": \"responder_direto\"|\"fora_contexto\"|\"chamar_agente\"|\"chamar_ferramenta\"|\"finalizar\", ...}";

        return prompt;
    }

    private void InjectClientReferences(List<(string role, string content)> transcript, string cliente)
    {
        var referencias = _referenciaLoader.ObterReferenciasTexto(cliente);
        if (!string.IsNullOrEmpty(referencias))
        {
            transcript.Add(("user", $"Referencias do cliente {cliente}:\n{referencias}"));
        }
    }

    private static string ComputeActionHash(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var acao = doc.RootElement.TryGetProperty("acao", out var a) ? a.GetString() : "";
            var agente = doc.RootElement.TryGetProperty("agente", out var ag) ? ag.GetString() : "";
            var briefing = doc.RootElement.TryGetProperty("briefing", out var br) ? br.GetString() : "";
            var ferramenta = doc.RootElement.TryGetProperty("ferramenta", out var fe) ? fe.GetString() : "";
            return $"{acao}|{agente}|{briefing}|{ferramenta}";
        }
        catch
        {
            return json;
        }
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
