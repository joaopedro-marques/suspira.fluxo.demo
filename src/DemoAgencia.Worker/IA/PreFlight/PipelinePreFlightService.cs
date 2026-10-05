using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.PreFlight;

public class PipelinePreFlightService
{
    private readonly IReferenciasCliente _referencias;
    private readonly IAgentesCatalogo _agentes;
    private readonly IServicoChat _servicoChat;
    private readonly EnriquecedorContextoCliente _enriquecedor;
    private readonly ConversaPendenteStore _store;
    private readonly PreFlightOptions _options;
    private readonly ILogger<PipelinePreFlightService> _logger;

    public PipelinePreFlightService(
        IReferenciasCliente referencias,
        IAgentesCatalogo agentes,
        IServicoChat servicoChat,
        EnriquecedorContextoCliente enriquecedor,
        ConversaPendenteStore store,
        IOptions<PreFlightOptions> options,
        ILogger<PipelinePreFlightService> logger)
    {
        _referencias = referencias;
        _agentes = agentes;
        _servicoChat = servicoChat;
        _enriquecedor = enriquecedor;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task<ResultadoPreFlight> IniciarAsync(
        long chatId,
        string mensagem,
        CancellationToken ct = default)
    {
        var refinador = _agentes.ObterPorNome("Refinador");
        if (refinador == null)
        {
            _logger.LogError("Agente Refinador nao encontrado");
            return ResultadoPreFlight.Falha();
        }

        var estado = new EstadoPreFlight
        {
            ChatId = chatId,
            MensagemOriginal = mensagem
        };

        var clienteDetectado = DetectarCliente(mensagem);
        if (!string.IsNullOrEmpty(clienteDetectado))
        {
            estado.Cliente = clienteDetectado;
            estado.ContextoCliente = await _enriquecedor.ObterContextoAsync(clienteDetectado, ct);
        }

        return await ExecutarRefinamentoAsync(estado, ct);
    }

    public virtual async Task<ResultadoPreFlight> ResumirAsync(
        long chatId,
        string resposta,
        CancellationToken ct = default)
    {
        var estado = _store.Obter(chatId);
        if (estado == null)
        {
            _logger.LogWarning("Nenhuma pendencia encontrada para chat {ChatId}", chatId);
            return ResultadoPreFlight.Falha();
        }

        estado.RespostasAcumuladas.Add(resposta);
        estado.RodadasPerguntas++;

        return await ExecutarRefinamentoAsync(estado, ct);
    }

    private async Task<ResultadoPreFlight> ExecutarRefinamentoAsync(EstadoPreFlight estado, CancellationToken ct)
    {
        var refinador = _agentes.ObterPorNome("Refinador");
        if (refinador == null)
            return ResultadoPreFlight.Falha();

        var promptRefinador = MontarPromptRefinador(estado);
        var respostaRefinador = await _servicoChat.ChamarAgenteAsync(
            estado.ChatId,
            refinador.Persona,
            refinador.ModeloAlvo,
            promptRefinador,
            "preflight_refinador",
            temperature: refinador.Temperatura,
            maxTokens: refinador.MaxTokens > 0 ? refinador.MaxTokens : 2000,
            ct: ct);

        var refinamento = ParserRefinamento.TentarExtrair(respostaRefinador);
        if (refinamento == null)
        {
            _logger.LogWarning("ParserRefinamento retornou nulo. Resposta: {Resposta}",
                respostaRefinador[..Math.Min(200, respostaRefinador.Length)]);
            return ResultadoPreFlight.Falha();
        }

        if (refinamento.PrecisaEsclarecimento)
        {
            if (estado.RodadasPerguntas >= _options.MaxRodadasPerguntas)
            {
                _logger.LogWarning("MaxRodadasPerguntas excedido para chat {ChatId}", estado.ChatId);
                return ResultadoPreFlight.Falha();
            }

            estado.PerguntasAtuais = refinamento.Perguntas;
            _store.Guardar(estado.ChatId, estado);
            return ResultadoPreFlight.PrecisaEsclarecimento(refinamento.Perguntas);
        }

        if (!string.IsNullOrEmpty(refinamento.Cliente) && string.IsNullOrEmpty(estado.Cliente))
        {
            var clienteLower = refinamento.Cliente.ToLowerInvariant();
            var registrados = _referencias.ListarClientes() ?? Array.Empty<string>();
            var registrado = registrados.Any(c => string.Equals(c, clienteLower, StringComparison.OrdinalIgnoreCase));

            if (registrado)
            {
                estado.Cliente = clienteLower;
                estado.ContextoCliente = await _enriquecedor.ObterContextoAsync(clienteLower, ct);
            }
            else if (estado.RodadasPerguntas >= 1 || estado.RodadasPerguntas >= _options.MaxRodadasPerguntas)
            {
                _logger.LogWarning("Cliente '{Cliente}' nao possui registros. Pedido bloqueado para chat {ChatId}", refinamento.Cliente, estado.ChatId);
                _store.Remover(estado.ChatId);
                return ResultadoPreFlight.Bloqueado($"Cliente '{refinamento.Cliente}' nao possui registros. Pedido bloqueado.");
            }
            else
            {
                estado.PerguntasAtuais = new List<string> { $"O cliente '{refinamento.Cliente}' nao possui registros. Confirme o cliente correto para prosseguir." };
                _store.Guardar(estado.ChatId, estado);
                return ResultadoPreFlight.PrecisaEsclarecimento(estado.PerguntasAtuais);
            }
        }

        if (refinamento.Simples)
        {
            _store.Remover(estado.ChatId);
            return ResultadoPreFlight.Concluido(estado.MensagemOriginal, estado.MensagemOriginal, estado.Cliente);
        }

        return await ExecutarMontadorAsync(estado, refinamento.PedidoRefinado ?? estado.MensagemOriginal, ct);
    }

    private async Task<ResultadoPreFlight> ExecutarMontadorAsync(
        EstadoPreFlight estado,
        string pedidoRefinado,
        CancellationToken ct)
    {
        var montador = _agentes.ObterPorNome("Montador de Briefing");
        if (montador == null)
        {
            _logger.LogError("Agente Montador de Briefing nao encontrado");
            return ResultadoPreFlight.Falha();
        }

        var promptMontador = MontarPromptMontador(estado, pedidoRefinado);
        var respostaMontador = await ChamarMontadorAsync(estado, montador, promptMontador, ct, "preflight_montador");

        var parse = ParserBriefing.TentarExtrairComDiagnostico(respostaMontador);
        if (parse.Resultado != null)
        {
            _store.Remover(estado.ChatId);
            return ResultadoPreFlight.Concluido(parse.Resultado.Briefing, estado.MensagemOriginal, estado.Cliente);
        }

        _logger.LogWarning("ParserBriefing falhou ({Motivo}). Resposta (inicio): {Inicio} | Resposta (fim): {Fim}",
            parse.MotivoFalha,
            respostaMontador[..Math.Min(200, respostaMontador.Length)],
            respostaMontador[^Math.Min(200, respostaMontador.Length)..]);

        string respostaRetry;
        if (string.IsNullOrWhiteSpace(respostaMontador))
        {
            var tokensOriginais = montador.MaxTokens > 0 ? montador.MaxTokens : 2000;
            var tokensDobrados = Math.Min(tokensOriginais * 2, 8000);
            _logger.LogWarning("Retry do Montador com tokens dobrados ({Tokens}) por resposta vazia", tokensDobrados);
            respostaRetry = await ChamarMontadorAsync(estado, montador, promptMontador, ct, "preflight_montador_retry", tokensDobrados);
        }
        else
        {
            var promptRetry = promptMontador + "\n\n## ATENCAO\nSua resposta anterior nao foi parseada como JSON valido. Responda APENAS com JSON valido, sem markdown, sem fences ```json, e sem aspas nao escapadas dentro dos textos.";
            respostaRetry = await ChamarMontadorAsync(estado, montador, promptRetry, ct, "preflight_montador_retry");
        }

        var parseRetry = ParserBriefing.TentarExtrairComDiagnostico(respostaRetry);
        if (parseRetry.Resultado != null)
        {
            _store.Remover(estado.ChatId);
            return ResultadoPreFlight.Concluido(parseRetry.Resultado.Briefing, estado.MensagemOriginal, estado.Cliente);
        }

        _logger.LogWarning("Retry do Montador tambem falhou ({Motivo}).", parseRetry.MotivoFalha);
        return ResultadoPreFlight.Falha();
    }

    private async Task<string> ChamarMontadorAsync(
        EstadoPreFlight estado,
        AgenteDefinicao montador,
        string prompt,
        CancellationToken ct,
        string etapaNome,
        int? maxTokensOverride = null)
    {
        var maxTokens = maxTokensOverride ?? (montador.MaxTokens > 0 ? montador.MaxTokens : 2000);
        var resposta = await _servicoChat.ChamarAgenteAsync(
            estado.ChatId,
            montador.Persona,
            montador.ModeloAlvo,
            prompt,
            etapaNome,
            temperature: montador.Temperatura,
            maxTokens: maxTokens,
            ct: ct);
        return resposta ?? string.Empty;
    }

    private string DetectarCliente(string mensagem)
    {
        var clientes = _referencias.ListarClientes();
        var mensagemLower = mensagem.ToLowerInvariant();

        foreach (var cliente in clientes)
        {
            if (mensagemLower.Contains(cliente.ToLowerInvariant()))
                return cliente;
        }

        return string.Empty;
    }

    private string MontarPromptRefinador(EstadoPreFlight estado)
    {
        var clientes = _referencias.ListarClientes();
        var prompt = $"## Mensagem do usuario\n{estado.MensagemOriginal}";

        if (clientes.Any())
            prompt += $"\n\n## Clientes cadastrados\n{string.Join(", ", clientes)}";

        if (!string.IsNullOrEmpty(estado.Cliente))
        {
            prompt += $"\n\n## Cliente identificado: {estado.Cliente}";
            if (!string.IsNullOrEmpty(estado.ContextoCliente))
                prompt += $"\n\n## Contexto do cliente\n{estado.ContextoCliente}";
        }

        if (estado.RespostasAcumuladas.Any())
        {
            prompt += "\n\n## Esclarecimentos do usuario";
            for (var i = 0; i < estado.PerguntasAtuais.Count; i++)
            {
                if (i < estado.RespostasAcumuladas.Count)
                    prompt += $"\nP: {estado.PerguntasAtuais[i]}\nR: {estado.RespostasAcumuladas[i]}";
            }
        }

        prompt += "\n\n## Instrucao\nAnalise a mensagem e o contexto. Se precisar de esclarecimento para entender a intencao, responda com as perguntas. Caso contrario, retorne o pedido refinado. Se for uma pergunta simples ou conversa casual, marque simples=true.";

        return prompt;
    }

    private string MontarPromptMontador(EstadoPreFlight estado, string pedidoRefinado)
    {
        var prompt = $"## Pedido refinado\n{pedidoRefinado}";

        if (!string.IsNullOrEmpty(estado.Cliente))
        {
            prompt += $"\n\n## Cliente: {estado.Cliente}";
            if (!string.IsNullOrEmpty(estado.ContextoCliente))
                prompt += $"\n\n## Contexto do cliente\n{estado.ContextoCliente}";
        }

        prompt += "\n\n## Instrucao\nMonte um briefing completo e autocontido para os agentes de producao. Inclua contexto do cliente (se houver) e descricao clara do que deve ser produzido (formato, plataforma, publico-alvo, tom).";

        return prompt;
    }
}
