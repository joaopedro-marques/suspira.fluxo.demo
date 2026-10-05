using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.Router;

public class RouterService
{
    private readonly IReferenciasCliente _referencias;
    private readonly IServicoChat _servicoChat;
    private readonly IAgentesCatalogo _catalogo;
    private readonly ConversaPendenteStore _store;
    private readonly PreFlightOptions _options;
    private readonly ILogger<RouterService> _logger;

    public RouterService(
        IReferenciasCliente referencias,
        IServicoChat servicoChat,
        IAgentesCatalogo catalogo,
        ConversaPendenteStore store,
        IOptions<PreFlightOptions> options,
        ILogger<RouterService> logger)
    {
        _referencias = referencias;
        _servicoChat = servicoChat;
        _catalogo = catalogo;
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
        var router = _catalogo.Obter("router");

        var resposta = await _servicoChat.ChamarAgenteAsync(
            estado.ChatId,
            router.Persona,
            router.Modelo,
            prompt,
            "router",
            temperature: router.Temperatura,
            maxTokens: router.MaxTokens,
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
            router.Persona,
            router.Modelo,
            retryPrompt,
            "router_retry",
            temperature: router.Temperatura,
            maxTokens: router.MaxTokens,
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
