using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

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
        IOptions<LoopOptions> options,
        IServicoChat openRouter,
        IAgentesCatalogo agenteLoader,
        IReferenciasCliente referenciaLoader,
        FerramentaRegistry ferramentaRegistry,
        EnriquecedorContextoCliente enriquecedor)
    {
        _logger = logger;
        _options = options.Value;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _referenciaLoader = referenciaLoader;
        _ferramentaRegistry = ferramentaRegistry;
        _gateQualidade = new GateQualidade(openRouter, agenteLoader);
        _promptBuilder = new PromptOrquestradorBuilder(agenteLoader, ferramentaRegistry);
        _enriquecedor = enriquecedor;
    }

    public virtual async Task<ResultadoPipeline> ExecutarAsync(
        long chatId,
        string mensagem,
        Func<string, Task>? onProgresso = null,
        CancellationToken ct = default,
        string? pedidoOriginal = null)
    {
        var context = new LoopContext
        {
            ChatId = chatId,
            Mensagem = mensagem,
            MensagemOriginal = pedidoOriginal,
            OnProgresso = onProgresso,
            CancellationToken = ct,
            MaxTurnos = _options.MaxTurnos,
            MaxRefacoesQa = _options.MaxRefacoesQa,
            MaxRetriesTransientes = _options.MaxRetriesTransientes
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

        var userMessage = !string.IsNullOrEmpty(pedidoOriginal)
            ? $"## Pedido original do usuario\n{pedidoOriginal}\n\n## Briefing de producao\n{mensagem}"
            : mensagem;
        transcript.Add(("user", userMessage));

        var jsonRetry = false;

        try
        {
            while (context.Turnos < context.MaxTurnos)
            {
            context.Turnos++;

            await NotificarProgresso(onProgresso, $"🧠 Turno {context.Turnos}...");

            var transcriptText = MontarTranscript(transcript, _options.MaxCharsContexto);
            var estadoTrabalho = EstadoTrabalhoBuilder.Build(context);
            transcriptText += "\n\n" + estadoTrabalho;

            var respostaOrquestrador = await ChamarComRetryTransienteAsync(
                () => _openRouter.ChamarAgenteAsync(
                    chatId,
                    orquestrador.Persona,
                    orquestrador.ModeloAlvo,
                    transcriptText,
                    "loop_orquestrador",
                    temperature: orquestrador.Temperatura,
                    maxTokens: _options.MaxTokensOrquestrador,
                    ct: ct),
                "loop_orquestrador",
                context,
                ct);

            var decisao = ParserDecisao.TentarExtrair(respostaOrquestrador);
            if (decisao == null)
            {
                if (!jsonRetry)
                {
                    jsonRetry = true;
                    transcript.Add(("assistant", respostaOrquestrador));
                    transcript.Add(("user", "Sua resposta nao continha JSON valido. Responda apenas com JSON."));
                    TentarRetryGratis(context);
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
                TentarRetryGratis(context);
                continue;
            }
            context.UltimaAcaoHash = acaoHash;

            if (!string.IsNullOrEmpty(decisao.Cliente) && string.IsNullOrEmpty(context.Cliente))
            {
                var registrados = _referenciaLoader.ListarClientes() ?? Array.Empty<string>();
                var registrado = registrados.Any(c => string.Equals(c, decisao.Cliente, StringComparison.OrdinalIgnoreCase));
                if (registrado)
                {
                    context.Cliente = decisao.Cliente;
                    await InjectClientReferencesAsync(transcript, decisao.Cliente, ct);
                }
                else
                {
                    _logger.LogWarning("Orquestrador atribuiu cliente '{Cliente}' nao registrado. Ignorado.", decisao.Cliente);
                }
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
                        TentarRetryGratis(context);
                        continue;
                    }

                    await NotificarProgresso(onProgresso, $"✍️ {agente.Nome} trabalhando...");
                    context.Resultado.EtapasExecutadas.Add($"loop_agente_{agente.Nome}");

                    var briefing = decisao.Briefing ?? "";
                    
                    if (context.Artefatos.Count > 0)
                    {
                        briefing += "\n\n## Trabalho previo de outros agentes\n";
                        var idsSelecionados = decisao.ArtefatosIds?.Count > 0
                            ? new HashSet<string>(decisao.ArtefatosIds)
                            : new HashSet<string> { context.Artefatos[^1].Id };

                        foreach (var art in context.Artefatos)
                        {
                            var incluirEntregavel = idsSelecionados.Contains(art.Id);
                            briefing += $"\n### {art.Agente} (artefato {art.Id})";
                            if (incluirEntregavel)
                                briefing += $":\n{art.Entregavel}\n";
                            else
                                briefing += "\n";
                            if (!string.IsNullOrEmpty(art.Notas))
                                briefing += $"Notas: {art.Notas}\n";
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

                    var output = await ChamarComRetryTransienteAsync(
                        () => _openRouter.ChamarAgenteAsync(
                            chatId,
                            agente.Persona,
                            agente.ModeloAlvo,
                            briefing,
                            $"loop_agente_{agente.Nome}",
                            temperature: agente.Temperatura,
                            maxTokens: agente.MaxTokens > 0 ? agente.MaxTokens : 2000,
                            ct: ct),
                        $"loop_agente_{agente.Nome}",
                        context,
                        ct);

                    context.UltimoAgente = agente.Nome;
                    context.UltimoOutputAgente = output;

                    var parsed = ParserOutputAgente.Extrair(output);
                    var entregavelAgente = parsed.Entregavel ?? string.Empty;
                    var notas = parsed.Notas;
                    var resumo = parsed.Resumo ?? (entregavelAgente.Length > 100 ? entregavelAgente[..100] + "..." : entregavelAgente);

                    var tipoArtefato = entregavelAgente.Contains("<html", StringComparison.OrdinalIgnoreCase)
                        || entregavelAgente.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                        ? TipoArtefato.Html
                        : TipoArtefato.Copy;
                    var artefato = Artefato.Criar(tipoArtefato, agente.Nome, entregavelAgente, resumo, notas);
                    context.AdicionarArtefato(artefato);

                    transcript.Add(("assistant", respostaOrquestrador));
                    var stubPartes = new List<string>
                    {
                        $"Agente {agente.Nome} produziu artefato {artefato.Id} ({tipoArtefato}, {entregavelAgente.Length} chars)"
                    };
                    if (!string.IsNullOrEmpty(resumo))
                        stubPartes.Add($"Resumo: {resumo}");
                    if (!string.IsNullOrEmpty(notas))
                        stubPartes.Add($"Notas: {notas}");
                    transcript.Add(("user", string.Join(". ", stubPartes)));
                    break;

                case "chamar_ferramenta":
                    var ferramenta = _ferramentaRegistry.Obter(decisao.Ferramenta ?? "");
                    if (ferramenta == null)
                    {
                        transcript.Add(("assistant", respostaOrquestrador));
                        transcript.Add(("user", $"Ferramenta '{decisao.Ferramenta}' nao encontrada."));
                        TentarRetryGratis(context);
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
                    var ultimoNaoInterno = context.Artefatos.LastOrDefault(a => !IsAgenteInterno(a.Agente));
                    var entregavel = !string.IsNullOrWhiteSpace(decisao.Entregavel)
                        ? decisao.Entregavel
                        : ultimoNaoInterno?.Entregavel
                          ?? (ultimoNaoInterno == null ? "" : context.UltimoOutputAgente ?? "");
                    context.Entregavel = entregavel;

                    if (!context.QaExecutado)
                    {
                        await NotificarProgresso(onProgresso, "🔍 Revisando qualidade...");
                        context.Resultado.EtapasExecutadas.Add("loop_qualidade");

                        var infoDeck = BuildInfoDeck(context);

                        var qaResultado = await ChamarComRetryTransienteAsync(
                            () => _gateQualidade.AvaliarAsync(
                                chatId,
                                context.Mensagem,
                                entregavel,
                                infoDeck,
                                context.MensagemOriginal,
                                ct),
                            "loop_qualidade",
                            context,
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
                    TentarRetryGratis(context);
                    break;
            }
        }
        }
        catch (Exception ex) when (IsFalhaTransiente(ex))
        {
            _logger.LogError(ex, "Falha transiente sustentada apos retries, encerrando graciosamente");
            context.Resultado.RespostaFinal = _options.MensagemFalha;
            return context.Resultado;
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

    private bool IsAgenteInterno(string nomeAgente)
    {
        var agente = _agenteLoader.ObterPorNome(nomeAgente);
        return agente?.Interno ?? false;
    }

    private static string BuildInfoDeck(LoopContext context)
    {
        if (context.ImagensDeck.Count == 0 && context.PlanoDeck.Count == 0)
            return "";

        var linhas = new List<string>();
        if (context.PlanoDeck.Count > 0)
        {
            var papeisGerados = new HashSet<string>(context.ImagensDeck.Select(i => i.Papel));
            var count = context.PlanoDeck.Count(p => papeisGerados.Contains(p));
            linhas.Add($"Plano: {count}/{context.PlanoDeck.Count} [{string.Join(", ", context.PlanoDeck)}]");
        }
        if (context.ImagensDeck.Count > 0)
        {
            var imagensStr = string.Join(", ", context.ImagensDeck.Select(i =>
            {
                var legenda = !string.IsNullOrEmpty(i.Legenda) ? $" \"{i.Legenda}\"" : "";
                return $"[{i.Id}] {i.Papel}{legenda}";
            }));
            linhas.Add($"Geradas: {imagensStr}");
        }
        return string.Join("\n", linhas);
    }

    private async Task<T> ChamarComRetryTransienteAsync<T>(
        Func<Task<T>> operacao,
        string etapa,
        LoopContext context,
        CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await operacao();
            }
            catch (Exception ex) when (IsFalhaTransiente(ex))
            {
                if (context.RetriesTransientes < context.MaxRetriesTransientes)
                {
                    context.RetriesTransientes++;
                    await NotificarProgresso(context.OnProgresso, $"\u23f3 Provedor ocupado, aguardando... ({context.RetriesTransientes}/{context.MaxRetriesTransientes})");
                    _logger.LogWarning(ex, "Falha transiente em {Etapa}. Retry {Retry}/{Max}", etapa, context.RetriesTransientes, context.MaxRetriesTransientes);
                    await Task.Delay(TimeSpan.FromSeconds(_options.DelayTransienteSegundos), ct);
                    continue;
                }
                _logger.LogError(ex, "Falha transiente em {Etapa} esgotou retries ({Max})", etapa, context.MaxRetriesTransientes);
                throw;
            }
        }
    }

    internal static bool IsFalhaTransiente(Exception ex)
    {
        if (ex is HttpOperationException httpOp)
        {
            var code = (int?)httpOp.StatusCode;
            return code is 429 or 408 || code >= 500;
        }
        if (ex is HttpRequestException)
            return true;
        return false;
    }

    private static void TentarRetryGratis(LoopContext context)
    {
        if (context.RetriesGratis < context.MaxRetriesGratis)
        {
            context.Turnos--;
            context.RetriesGratis++;
        }
    }
}
