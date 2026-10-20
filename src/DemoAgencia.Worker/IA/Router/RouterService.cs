using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Onboarding;
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
    private readonly BriefExtractor _briefExtractor;

    public RouterService(
        IReferenciasCliente referencias,
        IServicoChat servicoChat,
        IAgentesCatalogo catalogo,
        ConversaPendenteStore store,
        IOptions<PreFlightOptions> options,
        ILogger<RouterService> logger,
        BriefExtractor? briefExtractor = null)
    {
        _referencias = referencias;
        _servicoChat = servicoChat;
        _catalogo = catalogo;
        _store = store;
        _options = options.Value;
        _logger = logger;
        _briefExtractor = briefExtractor ?? new BriefExtractor();
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

        var estrategia = !string.IsNullOrEmpty(estado.Cliente) ? _referencias.ObterEstrategia(estado.Cliente) : null;
        var resultado = RouterParser.TentarExtrair(resposta, estrategia);
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

        resultado = RouterParser.TentarExtrair(retryResposta, estrategia);
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
            var cliente = resultado.Cliente ?? estado.Cliente ?? "";
            var estrategia = !string.IsNullOrEmpty(cliente) ? _referencias.ObterEstrategia(cliente) : null;

            if (estrategia != null)
            {
                var camposExtraidos = _briefExtractor.Extrair(estado.MensagemOriginal, estrategia);

                _logger.LogInformation("Router converteu 'esclarecimento' em 'onboarding' para chat {ChatId} com {QtdCampos} campos extraídos",
                    estado.ChatId, CountCampos(camposExtraidos));

                _store.Remover(estado.ChatId);
                return new RouterResultado(
                    Tipo: "onboarding",
                    Resposta: null,
                    Cliente: cliente,
                    Brief: null,
                    Motivo: null,
                    CamposExtraidos: camposExtraidos);
            }

            _logger.LogWarning("Router retornou 'esclarecimento' mas cliente não tem estratégia. Tratando como 'fora_contexto'.");
            _store.Remover(estado.ChatId);
            return new RouterResultado(
                Tipo: "fora_contexto",
                Resposta: "Não consegui entender seu pedido. Por favor, seja mais específico sobre o que deseja criar.",
                Cliente: null,
                Brief: null,
                Motivo: RouterResultado.Motivos.AssuntoForaEscopo);
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
            if (string.IsNullOrEmpty(cliente))
            {
                _logger.LogInformation("Guarda: cliente null para chat {ChatId} — convertendo para onboarding",
                    estado.ChatId);
                _store.Remover(estado.ChatId);
                return new RouterResultado(
                    Tipo: "onboarding",
                    Resposta: null,
                    Cliente: null,
                    Brief: resultado.Brief,
                    Motivo: null,
                    CamposExtraidos: null);
            }

            if (!_options.ClientesPermitidos.Contains(cliente, StringComparer.OrdinalIgnoreCase))
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

            var estrategia = _referencias.ObterEstrategia(cliente);
            if (estrategia != null)
            {
                var camposExtraidos = _briefExtractor.Extrair(estado.MensagemOriginal, estrategia);

                _logger.LogInformation("Router converteu 'producao' em 'onboarding' para chat {ChatId} com {QtdCampos} campos extraídos",
                    estado.ChatId, CountCampos(camposExtraidos));

                _store.Remover(estado.ChatId);
                return new RouterResultado(
                    Tipo: "onboarding",
                    Resposta: null,
                    Cliente: cliente,
                    Brief: resultado.Brief,
                    Motivo: null,
                    CamposExtraidos: camposExtraidos);
            }
        }

        _store.Remover(estado.ChatId);
        return resultado;
    }

    private static int CountCampos(CamposExtraidos campos)
    {
        var count = 0;
        if (!string.IsNullOrEmpty(campos.EtapaJornada)) count++;
        if (!string.IsNullOrEmpty(campos.Marco)) count++;
        if (!string.IsNullOrEmpty(campos.Tema)) count++;
        if (!string.IsNullOrEmpty(campos.Objetivo)) count++;
        return count;
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
        sb.AppendLine("Etapa da jornada e campo critico. So preencha se o usuario declarou explicitamente a etapa.");

        return sb.ToString();
    }
}
