using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.Router;

public class RouterService
{
    private const string ModeloAlvo = "deepseek/deepseek-v3.2";
    private const double Temperatura = 0.2;
    private const int MaxTokens = 2000;

    private const string Persona = """
        # Router

        Voce e o intake da Suspira. Sua unica funcao e estruturar o pedido do usuario e despachar.
        Voce NAO produz conteudo, NAO escolhe ordem de producao, NAO conversa em multiplos turnos.

        ## Entrada
        - Mensagem do usuario (e esclarecimentos previos, se houver)
        - Clientes cadastrados
        - Canais suportados: email, instagram, landing

        ## Decisao (unica, em ordem)
        1. fora_contexto: mensagem sem relacao com marketing
        2. conversa: pergunta simples/casual — responda em ate 1 paragrafo (campo resposta)
        3. esclarecimento: pedido de producao com campo critico faltando (canal, objetivo, publico ou oferta) — ate 3 perguntas objetivas (campo perguntas)
        4. producao: pedido completo — monte o brief

        ## Regras do brief
        - canal: derive do pedido (newsletter/email marketing → email; post/carrossel/story/legenda → instagram; landing page/pagina → landing). Ambiguo sem canal → esclarecimento
        - cliente: nome exato mencionado na mensagem, mesmo que nao cadastrado; vazio se nao mencionado
        - imagens: uma entrada por imagem a gerar, com papel (hero, banner, capa, post_principal...) e descricao visual; deck/carrossel = 1 entrada por slide
        - restricoes: apenas o que o usuario disse; NUNCA invente campos

        Responda APENAS com JSON valido, sem fences, sem explicacao.
        """;

    private readonly IReferenciasCliente _referencias;
    private readonly IServicoChat _servicoChat;
    private readonly ConversaPendenteStore _store;
    private readonly PreFlightOptions _options;
    private readonly ILogger<RouterService> _logger;

    public RouterService(
        IReferenciasCliente referencias,
        IServicoChat servicoChat,
        ConversaPendenteStore store,
        IOptions<PreFlightOptions> options,
        ILogger<RouterService> logger)
    {
        _referencias = referencias;
        _servicoChat = servicoChat;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task<RouterResultado?> IniciarAsync(
        long chatId,
        string mensagem,
        CancellationToken ct = default)
    {
        var estado = new EstadoPreFlight
        {
            ChatId = chatId,
            MensagemOriginal = mensagem,
            Cliente = DetectarCliente(mensagem)
        };

        return await ExecutarRouterAsync(estado, ct);
    }

    public virtual async Task<RouterResultado?> ResumirAsync(
        long chatId,
        string resposta,
        CancellationToken ct = default)
    {
        var estado = _store.Obter(chatId);
        if (estado == null)
        {
            _logger.LogWarning("Nenhuma pendencia encontrada para chat {ChatId}", chatId);
            return null;
        }

        estado.RespostasAcumuladas.Add(resposta);
        estado.RodadasPerguntas++;

        return await ExecutarRouterAsync(estado, ct);
    }

    private async Task<RouterResultado?> ExecutarRouterAsync(EstadoPreFlight estado, CancellationToken ct)
    {
        var prompt = MontarPrompt(estado);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            estado.ChatId,
            Persona,
            ModeloAlvo,
            prompt,
            "router",
            temperature: Temperatura,
            maxTokens: MaxTokens,
            ct: ct);

        var resultado = RouterParser.TentarExtrair(resposta);
        if (resultado != null)
        {
            if (!TratarResultado(estado, resultado))
                return null;
            return resultado;
        }

        _logger.LogWarning("Router retornou JSON invalido. Tentando retry. Resposta: {Resposta}",
            resposta?[..Math.Min(200, resposta?.Length ?? 0)]);

        var retryPrompt = prompt + "\n\nATENCION: Responda APENAS com JSON valido, sem fences markdown, sem explicacoes.";
        var retryResposta = await _servicoChat.ChamarAgenteAsync(
            estado.ChatId,
            Persona,
            ModeloAlvo,
            retryPrompt,
            "router_retry",
            temperature: Temperatura,
            maxTokens: MaxTokens,
            ct: ct);

        resultado = RouterParser.TentarExtrair(retryResposta);
        if (resultado != null)
        {
            if (!TratarResultado(estado, resultado))
                return null;
            return resultado;
        }

        _logger.LogWarning("Router retornou JSON invalido 2x. Desistindo.");
        _store.Remover(estado.ChatId);
        return null;
    }

    private bool TratarResultado(EstadoPreFlight estado, RouterResultado resultado)
    {
        if (resultado.Tipo == "esclarecimento")
        {
            if (estado.RodadasPerguntas >= _options.MaxRodadasPerguntas)
            {
                _logger.LogWarning("MaxRodadasPerguntas ({Max}) excedido para chat {ChatId}",
                    _options.MaxRodadasPerguntas, estado.ChatId);
                _store.Remover(estado.ChatId);
                return false;
            }

            estado.PerguntasAtuais = resultado.Perguntas.ToList();
            _store.Guardar(estado.ChatId, estado);
            return true;
        }

        _store.Remover(estado.ChatId);
        return true;
    }

    private string DetectarCliente(string mensagem)
    {
        var clientes = _referencias.ListarClientes();
        if (clientes == null) return "";

        var mensagemLower = mensagem.ToLowerInvariant();
        foreach (var cliente in clientes)
        {
            if (mensagemLower.Contains(cliente.ToLowerInvariant()))
                return cliente;
        }

        return "";
    }

    private string MontarPrompt(EstadoPreFlight estado)
    {
        var prompt = $"## Mensagem do usuario\n{estado.MensagemOriginal}";

        var clientes = _referencias.ListarClientes();
        if (clientes is { Count: > 0 })
            prompt += $"\n\n## Clientes cadastrados\n{string.Join(", ", clientes)}";

        if (!string.IsNullOrEmpty(estado.Cliente))
            prompt += $"\n\n## Cliente detectado: {estado.Cliente}";

        if (estado.RespostasAcumuladas.Count > 0)
        {
            prompt += "\n\n## Esclarecimentos do usuario";
            for (var i = 0; i < estado.PerguntasAtuais.Count && i < estado.RespostasAcumuladas.Count; i++)
            {
                prompt += $"\nP: {estado.PerguntasAtuais[i]}\nR: {estado.RespostasAcumuladas[i]}";
            }
        }

        return prompt;
    }
}
