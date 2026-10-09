using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Onboarding;

public class OnboardingService
{
    private static readonly List<string> Objetivos = new() { "vender", "engajar", "informar" };

    private readonly OnboardingStore _store;
    private readonly IReferenciasCliente _referencias;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        OnboardingStore store,
        IReferenciasCliente referencias,
        ILogger<OnboardingService> logger)
    {
        _store = store;
        _referencias = referencias;
        _logger = logger;
    }

    public PerguntaOnboarding? Iniciar(
        long chatId,
        string cliente,
        string mensagemOriginal,
        CamposExtraidos? extraidos = null)
    {
        var estrategia = _referencias.ObterEstrategia(cliente);
        if (estrategia == null)
        {
            _logger.LogWarning("Estrategia nao encontrada para cliente {Cliente}", cliente);
            return null;
        }

        var state = new OnboardingState
        {
            ChatId = chatId,
            Cliente = cliente,
            MensagemOriginal = mensagemOriginal,
            PassoAtual = OnboardingPasso.Etapa,
            CriadoEm = DateTimeOffset.UtcNow
        };

        AplicarPrePreenchimento(state, estrategia, extraidos);
        AvancarComAutoSelecao(state, estrategia);

        _store.Guardar(chatId, state);
        _logger.LogInformation("Onboarding iniciado para chat {ChatId} passo {Passo}", chatId, state.PassoAtual);

        return ObterPergunta(state, estrategia);
    }

    public PerguntaOnboarding? Responder(long chatId, string callbackData)
    {
        var state = _store.Obter(chatId);
        if (state == null) return null;

        var estrategia = _referencias.ObterEstrategia(state.Cliente);
        if (estrategia == null) return null;

        if (OnboardingCallbacks.EhAcaoEspecial(callbackData, out var acao))
            return ProcessarAcaoEspecial(state, estrategia, acao);

        if (!OnboardingCallbacks.TentarParse(callbackData, out var passo, out var valor))
        {
            _logger.LogWarning("Callback invalido para chat {ChatId}: {Callback}", chatId, callbackData);
            return null;
        }

        if (passo != state.PassoAtual)
        {
            _logger.LogWarning("Passo incompativel para chat {ChatId}: esperado {Esperado}, recebido {Recebido}",
                chatId, state.PassoAtual, passo);
            return null;
        }

        AplicarResposta(state, passo, valor);
        AvancarComAutoSelecao(state, estrategia);

        _store.Guardar(chatId, state);
        return ObterPergunta(state, estrategia);
    }

    public PerguntaOnboarding? ResponderTexto(long chatId, string texto)
    {
        var state = _store.Obter(chatId);
        if (state == null) return null;

        if (state.PassoAtual != OnboardingPasso.CtaLink)
            return null;

        state.CtaLink = texto.Trim();
        state.PassoAtual = OnboardingPasso.Materiais;

        var estrategia = _referencias.ObterEstrategia(state.Cliente);
        if (estrategia != null)
            AvancarComAutoSelecao(state, estrategia);

        _store.Guardar(chatId, state);
        return ObterPergunta(state, estrategia!);
    }

    public void AdicionarMaterialFoto(long chatId, byte[] bytes, string fileName, string? caption)
    {
        var state = _store.Obter(chatId);
        if (state == null || state.PassoAtual != OnboardingPasso.Materiais) return;

        state.Materiais.Add(new MaterialCliente(bytes, fileName, caption));
        _store.Guardar(chatId, state);
    }

    public void AdicionarMaterialLink(long chatId, string link)
    {
        var state = _store.Obter(chatId);
        if (state == null || state.PassoAtual != OnboardingPasso.Materiais) return;

        state.LinksExtras.Add(link);
        _store.Guardar(chatId, state);
    }

    public Brief? Confirmar(long chatId)
    {
        var state = _store.Obter(chatId);
        if (state == null) return null;

        var brief = new Brief(
            Canal: "email",
            Objetivo: state.Objetivo,
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: state.CtaLink,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: state.EtapaJornada,
            SubJornada: state.Marco,
            Tema: state.Tema,
            LinksExtras: state.LinksExtras.Count > 0 ? new List<string>(state.LinksExtras) : null);

        _store.Remover(chatId);
        _logger.LogInformation("Onboarding confirmado para chat {ChatId}", chatId);
        return brief;
    }

    public void Cancelar(long chatId)
    {
        _store.Remover(chatId);
        _logger.LogInformation("Onboarding cancelado para chat {ChatId}", chatId);
    }

    private void AplicarPrePreenchimento(OnboardingState state, EstrategiaCliente estrategia, CamposExtraidos? extraidos)
    {
        if (extraidos == null) return;

        if (!string.IsNullOrEmpty(extraidos.EtapaJornada) &&
            estrategia.Fases.ContainsKey(extraidos.EtapaJornada))
        {
            state.EtapaJornada = extraidos.EtapaJornada;
            state.PassoAtual = OnboardingPasso.Marco;
        }

        if (state.EtapaJornada != null && !string.IsNullOrEmpty(extraidos.Marco))
        {
            var marcos = GetMarcos(estrategia.Fases[state.EtapaJornada]);
            if (marcos.Contains(extraidos.Marco, StringComparer.OrdinalIgnoreCase))
            {
                state.Marco = extraidos.Marco;
                state.PassoAtual = OnboardingPasso.Tema;
            }
        }

        if (state.EtapaJornada != null && !string.IsNullOrEmpty(extraidos.Tema))
        {
            var temas = estrategia.Fases[state.EtapaJornada].Temas;
            if (temas.Contains(extraidos.Tema, StringComparer.OrdinalIgnoreCase))
            {
                state.Tema = extraidos.Tema;
                state.PassoAtual = OnboardingPasso.Objetivo;
            }
        }

        if (!string.IsNullOrEmpty(extraidos.Objetivo) &&
            Objetivos.Contains(extraidos.Objetivo.ToLowerInvariant()))
        {
            state.Objetivo = extraidos.Objetivo.ToLowerInvariant();
            state.PassoAtual = OnboardingPasso.CtaLink;
        }
    }

    private void AvancarComAutoSelecao(OnboardingState state, EstrategiaCliente estrategia)
    {
        while (true)
        {
            switch (state.PassoAtual)
            {
                case OnboardingPasso.Etapa:
                    var fases = estrategia.Fases.Keys.ToList();
                    if (fases.Count == 1)
                    {
                        state.EtapaJornada = fases[0];
                        state.PassoAtual = OnboardingPasso.Marco;
                        continue;
                    }
                    return;

                case OnboardingPasso.Marco:
                    if (state.EtapaJornada == null) return;
                    var faseMarco = estrategia.Fases[state.EtapaJornada];
                    var marcos = GetMarcos(faseMarco);
                    if (marcos.Count == 0)
                    {
                        state.Marco = null;
                        state.PassoAtual = OnboardingPasso.Tema;
                        continue;
                    }
                    if (marcos.Count == 1)
                    {
                        state.Marco = marcos[0];
                        state.PassoAtual = OnboardingPasso.Tema;
                        continue;
                    }
                    return;

                case OnboardingPasso.Tema:
                    if (state.EtapaJornada == null) return;
                    var faseTema = estrategia.Fases[state.EtapaJornada];
                    if (faseTema.Temas.Count == 0)
                    {
                        state.Tema = null;
                        state.PassoAtual = OnboardingPasso.Objetivo;
                        continue;
                    }
                    if (faseTema.Temas.Count == 1)
                    {
                        state.Tema = faseTema.Temas[0];
                        state.PassoAtual = OnboardingPasso.Objetivo;
                        continue;
                    }
                    return;

                case OnboardingPasso.Confirmacao:
                    return;

                default:
                    return;
            }
        }
    }

    private static void AplicarResposta(OnboardingState state, OnboardingPasso passo, string valor)
    {
        switch (passo)
        {
            case OnboardingPasso.Etapa:
                state.EtapaJornada = valor;
                state.PassoAtual = OnboardingPasso.Marco;
                break;
            case OnboardingPasso.Marco:
                state.Marco = valor;
                state.PassoAtual = OnboardingPasso.Tema;
                break;
            case OnboardingPasso.Tema:
                state.Tema = valor;
                state.PassoAtual = OnboardingPasso.Objetivo;
                break;
            case OnboardingPasso.Objetivo:
                state.Objetivo = valor.ToLowerInvariant();
                state.PassoAtual = OnboardingPasso.CtaLink;
                break;
        }
    }

    private PerguntaOnboarding? ProcessarAcaoEspecial(OnboardingState state, EstrategiaCliente estrategia, string acao)
    {
        switch (acao)
        {
            case "sem-link":
                if (state.PassoAtual != OnboardingPasso.CtaLink) return null;
                state.CtaLink = null;
                state.PassoAtual = OnboardingPasso.Materiais;
                _store.Guardar(state.ChatId, state);
                return ObterPergunta(state, estrategia);

            case "pular":
                if (state.PassoAtual != OnboardingPasso.Materiais) return null;
                state.PassoAtual = OnboardingPasso.Confirmacao;
                _store.Guardar(state.ChatId, state);
                return ObterPergunta(state, estrategia);

            case "confirmar":
                return null;

            case "recomecar":
                state.EtapaJornada = null;
                state.Marco = null;
                state.Tema = null;
                state.Objetivo = null;
                state.CtaLink = null;
                state.LinksExtras.Clear();
                state.Materiais.Clear();
                state.PassoAtual = OnboardingPasso.Etapa;
                AvancarComAutoSelecao(state, estrategia);
                _store.Guardar(state.ChatId, state);
                return ObterPergunta(state, estrategia);

            default:
                return null;
        }
    }

    internal PerguntaOnboarding ObterPergunta(OnboardingState state, EstrategiaCliente estrategia)
    {
        switch (state.PassoAtual)
        {
            case OnboardingPasso.Etapa:
                return CriarPerguntaOpcoes(
                    "Qual etapa da jornada?",
                    OnboardingPasso.Etapa,
                    estrategia.Fases.Keys.OrderBy(k => k).ToList());

            case OnboardingPasso.Marco:
                var faseMarco = estrategia.Fases[state.EtapaJornada!];
                return CriarPerguntaOpcoes(
                    "Qual marco da jornada?",
                    OnboardingPasso.Marco,
                    GetMarcos(faseMarco));

            case OnboardingPasso.Tema:
                var faseTema = estrategia.Fases[state.EtapaJornada!];
                return CriarPerguntaOpcoes(
                    "Qual tema do email?",
                    OnboardingPasso.Tema,
                    faseTema.Temas);

            case OnboardingPasso.Objetivo:
                return CriarPerguntaOpcoes(
                    "Qual o objetivo do email?",
                    OnboardingPasso.Objetivo,
                    Objetivos);

            case OnboardingPasso.CtaLink:
                return new PerguntaOnboarding(
                    "Qual o link do CTA? (envie a URL ou clique em Sem link)",
                    OnboardingTipoPasso.TextoLivre,
                    new List<OpcaoOnboarding>
                    {
                        new("Sem link", OnboardingCallbacks.SemLink)
                    });

            case OnboardingPasso.Materiais:
                return new PerguntaOnboarding(
                    "Envie fotos ou links que quer inserir no email, ou clique em Pular.",
                    OnboardingTipoPasso.Coleta,
                    new List<OpcaoOnboarding>
                    {
                        new("Pular", OnboardingCallbacks.Pular)
                    });

            case OnboardingPasso.Confirmacao:
                return CriarPerguntaConfirmacao(state);

            default:
                return CriarPerguntaOpcoes(
                    "Qual etapa da jornada?",
                    OnboardingPasso.Etapa,
                    estrategia.Fases.Keys.OrderBy(k => k).ToList());
        }
    }

    private static PerguntaOnboarding CriarPerguntaOpcoes(string texto, OnboardingPasso passo, List<string> valores)
    {
        var opcoes = valores.Select(v => new OpcaoOnboarding(
            FormatLabel(v),
            OnboardingCallbacks.Criar(passo, v))).ToList();
        return new PerguntaOnboarding(texto, OnboardingTipoPasso.Opcoes, opcoes);
    }

    private static PerguntaOnboarding CriarPerguntaConfirmacao(OnboardingState state)
    {
        var linhas = new List<string> { "Resumo do briefing:" };

        if (!string.IsNullOrEmpty(state.EtapaJornada))
            linhas.Add($"• Etapa: {FormatLabel(state.EtapaJornada)}");
        if (!string.IsNullOrEmpty(state.Marco))
            linhas.Add($"• Marco: {state.Marco}");
        if (!string.IsNullOrEmpty(state.Tema))
            linhas.Add($"• Tema: {state.Tema}");
        if (!string.IsNullOrEmpty(state.Objetivo))
            linhas.Add($"• Objetivo: {FormatLabel(state.Objetivo)}");
        if (!string.IsNullOrEmpty(state.CtaLink))
            linhas.Add($"• CTA: {state.CtaLink}");
        else
            linhas.Add("• CTA: sem link");

        if (state.Materiais.Count > 0)
            linhas.Add($"• Materiais: {state.Materiais.Count} arquivo(s)");
        if (state.LinksExtras.Count > 0)
            linhas.Add($"• Links extras: {string.Join(", ", state.LinksExtras)}");

        return new PerguntaOnboarding(
            string.Join("\n", linhas),
            OnboardingTipoPasso.Confirmacao,
            new List<OpcaoOnboarding>
            {
                new("Confirmar", OnboardingCallbacks.Confirmar),
                new("Recomeçar", OnboardingCallbacks.Recomecar)
            });
    }

    private static string FormatLabel(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return valor;
        return char.ToUpper(valor[0]) + valor[1..];
    }

    internal static List<string> GetMarcos(FaseEstrategia fase)
    {
        return fase.SubJornadas.Values.SelectMany(v => v).ToList();
    }
}
