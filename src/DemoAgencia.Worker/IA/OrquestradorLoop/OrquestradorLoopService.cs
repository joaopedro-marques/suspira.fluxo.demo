using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class OrquestradorLoopService
{
    private readonly ILogger<OrquestradorLoopService> _logger;
    private readonly LoopOptions _options;
    private readonly IServicoChat _openRouter;
    private readonly IAgentesCatalogo _agenteLoader;
    private readonly IReferenciasCliente _referenciaLoader;
    private readonly FerramentaRegistry _ferramentaRegistry;
    private readonly GateQualidade _gateQualidade;
    private readonly PromptOrquestradorBuilder _promptBuilder;
    private readonly EnriquecedorContextoCliente _enriquecedor;

    public OrquestradorLoopService(
        ILogger<OrquestradorLoopService> logger,
        ILoggerFactory loggerFactory,
        IOptions<LoopOptions> options,
        IServicoChat openRouter,
        IAgentesCatalogo agenteLoader,
        IReferenciasCliente referenciaLoader,
        FerramentaRegistry ferramentaRegistry,
        IAnalisadorImagem analisadorImagem)
    {
        _logger = logger;
        _options = options.Value;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _referenciaLoader = referenciaLoader;
        _ferramentaRegistry = ferramentaRegistry;
        _gateQualidade = new GateQualidade(openRouter, agenteLoader);
        _promptBuilder = new PromptOrquestradorBuilder(agenteLoader, ferramentaRegistry);
        var enriquecedorLogger = loggerFactory.CreateLogger<EnriquecedorContextoCliente>();
        _enriquecedor = new EnriquecedorContextoCliente(referenciaLoader, analisadorImagem, enriquecedorLogger);
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
            MaxTurnos = _options.MaxTurnos,
            MaxRefacoesQa = _options.MaxRefacoesQa
        };

        var orquestrador = _agenteLoader.ObterPorPapel("orquestrador");
        if (orquestrador == null)
        {
            _logger.LogError("Orquestrador nao encontrado");
            context.Resultado.RespostaFinal = _options.MensagemFalha;
            return context.Resultado;
        }

        var transcript = new List<(string role, string content)>();
        transcript.Add(("system", _promptBuilder.Build(orquestrador)));
        transcript.Add(("user", mensagem));

        var jsonRetry = false;

        while (context.Turnos < context.MaxTurnos)
        {
            context.Turnos++;

            await NotificarProgresso(onProgresso, $"🧠 Turno {context.Turnos}...");

            var transcriptText = MontarTranscript(transcript, _options.MaxCharsContexto);
            var estadoTrabalho = EstadoTrabalhoBuilder.Build(context);
            transcriptText += "\n\n" + estadoTrabalho;

            var respostaOrquestrador = await _openRouter.ChamarAgenteAsync(
                chatId,
                orquestrador.Persona,
                orquestrador.ModeloAlvo,
                transcriptText,
                "loop_orquestrador",
                temperature: orquestrador.Temperatura,
                maxTokens: _options.MaxTokensOrquestrador,
                ct: ct);

            var decisao = ParserDecisao.TentarExtrair(respostaOrquestrador);
            if (decisao == null)
            {
                if (!jsonRetry)
                {
                    jsonRetry = true;
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "Sua resposta nao continha JSON valido. Responda apenas com JSON."));
                    continue;
                }
                _logger.LogWarning("Orquestrador retornou JSON invalido 2x seguidas. Turno {Turno}, resposta {Length} chars. Trecho: {Trecho}",
                    context.Turnos, respostaOrquestrador.Length, respostaOrquestrador[..Math.Min(200, respostaOrquestrador.Length)]);
                context.Resultado.RespostaFinal = _options.MensagemFalha;
                return context.Resultado;
            }

            jsonRetry = false;

            var acaoHash = ComputeActionHash(respostaOrquestrador);
            if (acaoHash == context.UltimaAcaoHash && decisao.Acao != "finalizar")
            {
                transcript.Add(("assistant", respostaOrquestrador));
                transcript.Add(("user", "Voce ja executou essa mesma acao. Tente uma abordagem diferente."));
                continue;
            }
            context.UltimaAcaoHash = acaoHash;

            if (!string.IsNullOrEmpty(decisao.Cliente) && string.IsNullOrEmpty(context.Cliente))
            {
                context.Cliente = decisao.Cliente;
                await InjectClientReferencesAsync(transcript, decisao.Cliente, ct);
            }

            switch (decisao.Acao)
            {
                case "fora_contexto":
                    context.Resultado.RespostaFinal = _options.MensagemForaContexto;
                    return context.Resultado;

                case "responder_direto":
                    context.Resultado.RespostaFinal = decisao.Resposta ?? "";
                    return context.Resultado;

                case "chamar_agente":
                    var agente = _agenteLoader.ObterPorNome(decisao.Agente ?? "");
                    if (agente == null)
                    {
                        transcript.Add(("assistant", respostaOrquestrador));
                        transcript.Add(("user", $"Agente '{decisao.Agente}' nao encontrado. Escolha um agente valido."));
                        continue;
                    }

                    await NotificarProgresso(onProgresso, $"✍️ {agente.Nome} trabalhando...");
                    context.Resultado.EtapasExecutadas.Add($"loop_agente_{agente.Nome}");

                    var briefing = decisao.Briefing ?? "";
                    
                    if (context.Artefatos.Count > 0)
                    {
                        briefing += "\n\n## Trabalho previo de outros agentes\n";
                        foreach (var art in context.Artefatos)
                        {
                            briefing += $"\n### Output do {art.Agente} (artefato {art.Id}):\n{art.Entregavel}\n";
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(context.Cliente))
                    {
                        var refsCliente = _referenciaLoader.ObterReferenciasTexto(context.Cliente);
                        if (!string.IsNullOrEmpty(refsCliente))
                        {
                            briefing += $"\n\n## Referencias do cliente {context.Cliente}\n{refsCliente}";
                        }
                    }

                    var output = await _openRouter.ChamarAgenteAsync(
                        chatId,
                        agente.Persona,
                        agente.ModeloAlvo,
                        briefing,
                        $"loop_agente_{agente.Nome}",
                        temperature: agente.Temperatura,
                        maxTokens: agente.MaxTokens > 0 ? agente.MaxTokens : 2000,
                        ct: ct);

                    context.UltimoAgente = agente.Nome;
                    context.UltimoOutputAgente = output;

                    var tipoArtefato = output.Contains("<html", StringComparison.OrdinalIgnoreCase)
                        || output.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                        ? TipoArtefato.Html
                        : TipoArtefato.Copy;
                    var resumo = output.Length > 100 ? output[..100] + "..." : output;
                    var artefato = Artefato.Criar(tipoArtefato, agente.Nome, output, resumo);
                    context.AdicionarArtefato(artefato);

                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", $"Agente {agente.Nome} produziu artefato {artefato.Id} ({tipoArtefato}, {output.Length} chars). Veja o estado do trabalho para detalhes."));
                    break;

                case "chamar_ferramenta":
                    var ferramenta = _ferramentaRegistry.Obter(decisao.Ferramenta ?? "");
                    if (ferramenta == null)
                    {
                        transcript.Add(("assistant", respostaOrquestrador));
                        transcript.Add(("user", $"Ferramenta '{decisao.Ferramenta}' nao encontrada."));
                        continue;
                    }

                    await NotificarProgresso(onProgresso, $"🔧 Executando {ferramenta.Nome}...");

                    var parametros = decisao.Parametros ?? JsonDocument.Parse("{}").RootElement;

                    var toolResult = await ferramenta.ExecutarAsync(context, parametros, ct);

                    var toolResultTruncado = Truncar(toolResult, _options.MaxCharsResultado);
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", $"Resultado da ferramenta {ferramenta.Nome}: {toolResultTruncado}"));
                    break;

                case "finalizar":
                    var entregavel = !string.IsNullOrWhiteSpace(decisao.Entregavel)
                        ? decisao.Entregavel
                        : context.Artefatos.LastOrDefault()?.Entregavel ?? context.UltimoOutputAgente ?? "";
                    context.Entregavel = entregavel;

                    if (!context.QaExecutado)
                    {
                        await NotificarProgresso(onProgresso, "🔍 Revisando qualidade...");
                        context.Resultado.EtapasExecutadas.Add("loop_qualidade");

                        var qaResultado = await _gateQualidade.AvaliarAsync(
                            chatId,
                            context.Mensagem,
                            entregavel,
                            ct);

                        context.QaAprovado = qaResultado.Aprovado;
                        context.FeedbackQa = qaResultado.Feedback;
                        context.QaExecutado = true;

                        if (!context.QaAprovado)
                        {
                            context.RefacoesQa++;
                            if (context.RefacoesQa >= context.MaxRefacoesQa)
                            {
                                _logger.LogWarning("MaxRefacoesQa excedido. Refacoes: {Refacoes}/{Max}. Feedback QA: {Feedback}",
                                    context.RefacoesQa, context.MaxRefacoesQa, context.FeedbackQa);
                                context.Resultado.RespostaFinal = _options.MensagemFalha;
                                return context.Resultado;
                            }

                            transcript.Add(("assistant", respostaOrquestrador));
                            transcript.Add(("user", $"QA reprovou. Feedback: {context.FeedbackQa}. Melhore o entregavel."));
                            context.QaExecutado = false;
                            continue;
                        }
                    }

                    context.Resultado.RespostaFinal = entregavel;
                    return context.Resultado;

                default:
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "Acao desconhecida. Use: responder_direto, fora_contexto, chamar_agente, chamar_ferramenta, finalizar."));
                    break;
            }
        }

        _logger.LogWarning("MaxTurnos esgotado. Turnos: {Turnos}/{Max}, ultima acao: {Acao}",
            context.Turnos, context.MaxTurnos, context.UltimaAcaoHash);
        context.Resultado.RespostaFinal = _options.MensagemFalha;
        return context.Resultado;
    }

    private async Task InjectClientReferencesAsync(List<(string role, string content)> transcript, string cliente, CancellationToken ct)
    {
        var contexto = await _enriquecedor.ObterContextoAsync(cliente, ct);
        if (!string.IsNullOrEmpty(contexto))
        {
            transcript.Add(("user", $"Referencias do cliente {cliente}:\n{contexto}"));
        }
    }

    private static string ComputeActionHash(string texto)
    {
        var decisao = ParserDecisao.TentarExtrair(texto);
        if (decisao == null)
            return texto;
        var parametros = decisao.Parametros?.GetRawText() ?? "";
        return $"{decisao.Acao}|{decisao.Agente}|{decisao.Briefing}|{decisao.Ferramenta}|{parametros}";
    }

    private static string MontarTranscript(List<(string role, string content)> transcript, int maxChars)
    {
        var texto = string.Join("\n", transcript.Select(t => $"{t.role}: {t.content}"));
        if (texto.Length <= maxChars)
            return texto;

        var cabecalho = transcript.Count >= 2
            ? $"{transcript[0].role}: {transcript[0].content}\n{transcript[1].role}: {transcript[1].content}"
            : $"{transcript[0].role}: {transcript[0].content}";

        var separador = "\n... [contexto anterior truncado] ...\n";
        var budgetRecentes = maxChars - cabecalho.Length - separador.Length;
        if (budgetRecentes <= 0)
            return Truncar(texto, maxChars);

        var recentes = new List<string>();
        for (var i = transcript.Count - 1; i >= 2; i--)
        {
            var linha = $"{transcript[i].role}: {transcript[i].content}";
            var teste = string.Join("\n", recentes.Prepend(linha));
            if (teste.Length > budgetRecentes)
                break;
            recentes.Insert(0, linha);
        }

        if (recentes.Count == 0)
            return Truncar(texto, maxChars);

        return cabecalho + separador + string.Join("\n", recentes);
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }

    private static string Truncar(string texto, int maxChars)
    {
        if (string.IsNullOrEmpty(texto) || texto.Length <= maxChars)
            return texto;
        var sufixo = "... [truncado]";
        var charsDisponiveis = maxChars - sufixo.Length;
        if (charsDisponiveis <= 0)
            return texto[..maxChars];
        return texto[..charsDisponiveis] + sufixo;
    }
}
