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
            var tratado = TratarResultado(estado, resultado);
            if (tratado == null) return null;
            return tratado;
        }

        _logger.LogWarning("Router retornou JSON invalido. Tentando retry. Resposta: {Resposta}",
            resposta?[..Math.Min(200, resposta?.Length ?? 0)]);

        var retryPrompt = prompt + """

            ATENCAO: Responda APENAS com JSON valido na raiz (sem envelope "response"), sem fences markdown, sem explicacoes.
            Campo obrigatorio: "tipo" com um destes valores: "fora_contexto", "conversa", "esclarecimento", "producao".
            """;
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
            var tratado = TratarResultado(estado, resultado);
            if (tratado == null) return null;
            return tratado;
        }

        _logger.LogWarning("Router retornou JSON invalido 2x. Desistindo.");
        _store.Remover(estado.ChatId);
        return null;
    }

    private RouterResultado? TratarResultado(EstadoPreFlight estado, RouterResultado resultado)
    {
        if (resultado.Tipo == "esclarecimento")
        {
            if (estado.RodadasPerguntas >= _options.MaxRodadasPerguntas)
            {
                _logger.LogWarning("MaxRodadasPerguntas ({Max}) excedido para chat {ChatId}",
                    _options.MaxRodadasPerguntas, estado.ChatId);
                _store.Remover(estado.ChatId);
                return null;
            }

            estado.PerguntasAtuais = resultado.Perguntas.ToList();
            _store.Guardar(estado.ChatId, estado);
            return resultado;
        }

        if (resultado.Tipo == "producao" && resultado.Brief != null)
        {
            if (!_options.CanaisPermitidos.Contains(resultado.Brief.Canal, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Guarda: canal {Canal} fora do escopo permitido para chat {ChatId}",
                    resultado.Brief.Canal, estado.ChatId);
                _store.Remover(estado.ChatId);
                return resultado with
                {
                    Tipo = "fora_contexto",
                    Resposta = null,
                    Brief = null,
                    Motivo = RouterResultado.Motivos.CanalNaoPermitido
                };
            }

            var cliente = resultado.Cliente ?? "";
            if (string.IsNullOrEmpty(cliente) ||
                !_options.ClientesPermitidos.Contains(cliente, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Guarda: cliente {Cliente} fora do escopo permitido para chat {ChatId}",
                    cliente, estado.ChatId);
                _store.Remover(estado.ChatId);
                return resultado with
                {
                    Tipo = "fora_contexto",
                    Resposta = null,
                    Brief = null,
                    Motivo = RouterResultado.Motivos.ClienteNaoPermitido
                };
            }
        }

        _store.Remover(estado.ChatId);
        return resultado;
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

        var clientesPermitidos = _referencias.ListarClientes()
            .Where(c => _options.ClientesPermitidos.Contains(c.ToLowerInvariant()))
            .ToList();
        if (clientesPermitidos.Count > 0)
            prompt += $"\n\n## Clientes atendidos\n{string.Join(", ", clientesPermitidos)}";

        prompt += $"\n\n## Canais atendidos\n{string.Join(", ", _options.CanaisPermitidos)}";

        if (!string.IsNullOrEmpty(estado.Cliente))
            prompt += $"\n\n## Cliente detectado: {estado.Cliente}";

        if (!string.IsNullOrEmpty(estado.Cliente) &&
            _options.ClientesPermitidos.Contains(estado.Cliente.ToLowerInvariant()))
        {
            var estrategia = _referencias.ObterEstrategia(estado.Cliente);
            if (estrategia != null)
                prompt += $"\n\n{MontarEstrategiaPrompt(estrategia)}";
        }

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

    private static string MontarEstrategiaPrompt(EstrategiaCliente estrategia)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Estrategia do cliente");
        sb.AppendLine();
        sb.AppendLine("Etapas de jornada disponiveis:");

        foreach (var (fase, dados) in estrategia.Fases)
        {
            sb.Append($"- {fase}");
            if (dados.Temas.Count > 0)
                sb.Append($" (temas: {string.Join(", ", dados.Temas)})");
            if (dados.SubJornadas.Count > 0)
            {
                var subJornadas = dados.SubJornadas.Values.SelectMany(v => v);
                sb.Append($" (sub-jornadas: {string.Join(", ", subJornadas)})");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Etapa da jornada e campo critico. So preencha se o usuario declarou explicitamente a etapa; caso contrario, pergunte via esclarecimento.");

        return sb.ToString();
    }
}
