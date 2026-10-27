using System.IO.Compression;
using System.Text;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ajustes;
using DemoAgencia.Worker.IA.Onboarding;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Seguranca;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DemoAgencia.Worker.Telegram;

public class TelegramService : BackgroundService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly TelegramOptions _options;
    private readonly ConcorrenciaOptions _concorrencia;
    private readonly PreFlightOptions _preFlightOptions;
    private readonly AjustesOptions _ajustesOptions;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly RateLimiterService _rateLimiter;
    private readonly ITelegramGatewayFactory _gatewayFactory;
    private readonly RouterService _router;
    private readonly ConversaPendenteStore _pendencias;
    private readonly OnboardingService _onboarding;
    private readonly OnboardingStore _onboardingStore;
    private readonly EntregaStore _entregas;
    private readonly RefinadorService _refinador;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UpdateDispatcher> _dispatcherLogger;
    private ITelegramGateway? _gateway;
    private UpdateDispatcher? _dispatcher;
    private SemaphoreSlim? _pipelineLimiter;

    public TelegramService(
        ILogger<TelegramService> logger,
        IOptions<TelegramOptions> options,
        IOptions<ConcorrenciaOptions> concorrencia,
        IOptions<PreFlightOptions> preFlightOptions,
        IAnalisadorImagem analisadorImagem,
        RateLimiterService rateLimiter,
        ITelegramGatewayFactory gatewayFactory,
        RouterService router,
        ConversaPendenteStore pendencias,
        OnboardingService onboarding,
        OnboardingStore onboardingStore,
        EntregaStore entregas,
        RefinadorService refinador,
        IOptions<AjustesOptions> ajustesOptions,
        IServiceProvider serviceProvider,
        ILogger<UpdateDispatcher> dispatcherLogger)
    {
        _logger = logger;
        _options = options.Value;
        _concorrencia = concorrencia.Value;
        _preFlightOptions = preFlightOptions.Value;
        _analisadorImagem = analisadorImagem;
        _rateLimiter = rateLimiter;
        _gatewayFactory = gatewayFactory;
        _router = router;
        _pendencias = pendencias;
        _onboarding = onboarding;
        _onboardingStore = onboardingStore;
        _entregas = entregas;
        _refinador = refinador;
        _ajustesOptions = ajustesOptions.Value;
        _serviceProvider = serviceProvider;
        _dispatcherLogger = dispatcherLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = _options.BotToken;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogError("Token do Telegram nao configurado. Defina Telegram__BotToken.");
            return;
        }

        _gateway = _gatewayFactory.Create(botToken);
        _pipelineLimiter = new SemaphoreSlim(_concorrencia.MaxPipelinesSimultaneos, _concorrencia.MaxPipelinesSimultaneos);
        _dispatcher = new UpdateDispatcher(ProcessUpdate, _concorrencia.CapacidadeFilaPorChat, _dispatcherLogger);

        try
        {
            var me = await _gateway.GetMeAsync(stoppingToken);
            _logger.LogInformation("Bot conectado: @{Username} (ID: {Id})", me.Username, me.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao conectar com Telegram");
            return;
        }

        var offset = 0;
        var retryDelay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _gateway.GetUpdatesAsync(offset, 30, stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
                    if (chatId.HasValue && _dispatcher!.TemFilaPendente(chatId.Value))
                    {
                        try
                        {
                            await _gateway.SendMessageAsync(chatId.Value,
                                "⏳ Aguarde, ainda estou processando seu pedido anterior...", stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Falha ao enviar aviso de fila para chat {ChatId}", chatId);
                        }
                    }

                    await _dispatcher!.EnfileirarAsync(update, stoppingToken);
                }

                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Polling encerrado graciosamente");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no polling. Tentando reconectar em {Delay}s", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
                var newDelay = retryDelay * 2;
                retryDelay = newDelay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : newDelay;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher != null)
            await _dispatcher.DrainAsync(TimeSpan.FromSeconds(_concorrencia.TimeoutDrainSegundos));
        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessUpdate(Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } callback)
            {
                await HandleCallbackQuery(callback, ct);
                return;
            }

            if (update.Message is { } message)
            {
                if (!_rateLimiter.PodeProcessar(message.Chat.Id))
                {
                    await _gateway!.SendMessageAsync(message.Chat.Id, "Voce esta enviando mensagens muito rapido. Aguarde um momento.", ct);
                    return;
                }

                if (message.Photo is { Length: > 0 })
                {
                    await HandlePhoto(message, ct);
                }
                else if (message.Text?.StartsWith("/") == true)
                {
                    await HandleCommand(message, ct);
                }
                else if (message.Text is { } text)
                {
                    await HandleTextMessage(message, text, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro processando update {UpdateId}", update.Id);
        }
    }

    private async Task HandleCommand(Message message, CancellationToken ct)
    {
        var parts = message.Text!.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();

        if (command == "/start")
        {
            await _gateway!.SendMessageAsync(message.Chat.Id, "Bem-vindo! Sou o DemoAgencia Bot. Use /help para ver os comandos disponiveis.", ct);
            return;
        }

        if (command == "/help")
        {
            var clientesTexto = _preFlightOptions.ClientesPermitidos.Count > 0
                ? string.Join(", ", _preFlightOptions.ClientesPermitidos)
                : "os clientes configurados";
            var exemploCliente = _preFlightOptions.ClientesPermitidos.Count > 0 
                ? _preFlightOptions.ClientesPermitidos[0] 
                : "CLIENTE";
            var help = $"""
                Comandos:
                /start - Inicia o bot
                /help - Mostra esta ajuda

                Envie uma mensagem livre para criar um email marketing para {clientesTexto}.
                Exemplo: "Crie um email para a {exemploCliente} sobre pos-compra"
                """;
            await _gateway!.SendMessageAsync(message.Chat.Id, help, ct);
            return;
        }

        _pendencias.Remover(message.Chat.Id);
        await _gateway!.SendMessageAsync(message.Chat.Id, "Comando nao reconhecido. Use /help para ver os comandos disponiveis.", ct);
    }

    private async Task HandleTextMessage(Message message, string text, CancellationToken ct)
    {
        var entrega = _entregas.Obter(message.Chat.Id);
        if (entrega != null)
        {
            if (entrega.Fase == FaseAjuste.AguardandoFeedback)
            {
                await ProcessarFeedbackAjusteAsync(message.Chat.Id, text, ct);
                return;
            }

            if (entrega.Fase == FaseAjuste.AguardandoConfirmacao)
            {
                await _gateway!.SendMessageAsync(message.Chat.Id,
                    "Use os botões acima para aplicar ou cancelar o ajuste.", ct);
                return;
            }
        }

        var onboardingAtivo = _onboardingStore.Obter(message.Chat.Id);
        if (onboardingAtivo != null)
        {
            await HandleTextoOnboarding(message, text, ct);
            return;
        }

        var pendente = _pendencias.Obter(message.Chat.Id);
        RouterResultado? resultadoRouter;
        int? mensagemProgressoId = null;

        try
        {
            var msg = await _gateway!.SendMessageAsync(message.Chat.Id, "🧠 Analisando seu pedido...", ct);
            mensagemProgressoId = msg.MessageId;

            resultadoRouter = await _router.IniciarAsync(message.Chat.Id, text, ct);

            await ProcessarResultadoRouter(message.Chat.Id, resultadoRouter, mensagemProgressoId.Value, text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar mensagem no pipeline");
            await NotificarErroAsync(message.Chat.Id, mensagemProgressoId, ct);
        }
    }

    private async Task HandleTextoOnboarding(Message message, string text, CancellationToken ct)
    {
        var estado = _onboardingStore.Obter(message.Chat.Id);
        if (estado == null) return;

        if (estado.PassoAtual == OnboardingPasso.Materiais)
        {
            var link = DetectarLink(text);
            if (link != null)
            {
                _onboarding.AdicionarMaterialLink(message.Chat.Id, link);
                await _gateway!.SendMessageAsync(message.Chat.Id, "🔗 Link adicionado. Envie mais ou clique em Pular.", ct);
                return;
            }

            await _gateway!.SendMessageAsync(message.Chat.Id, "Não identifiquei um link. Envie uma URL válida, uma foto ou clique em Pular.", ct);
            return;
        }

        if (estado.PassoAtual is OnboardingPasso.Oferta or OnboardingPasso.Publico or OnboardingPasso.CtaLink)
        {
            var pergunta = _onboarding.ResponderTexto(message.Chat.Id, text);
            if (pergunta != null)
                await EnviarPerguntaOnboardingAsync(message.Chat.Id, pergunta, ct);
            return;
        }
    }

    private async Task NotificarErroAsync(long chatId, int? mensagemProgressoId, CancellationToken ct)
    {
        if (mensagemProgressoId == null)
            return;

        const string mensagemErro = "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.";
        try
        {
            await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId.Value, mensagemErro, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao editar mensagem de erro (chat {ChatId}), enviando como nova mensagem", chatId);
            await _gateway!.SendMessageAsync(chatId, mensagemErro, ct);
        }
    }

    private async Task ProcessarResultadoRouter(long chatId, RouterResultado? resultado, int mensagemProgressoId, string mensagemOriginal, CancellationToken ct)
    {
        if (resultado == null)
        {
            await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao consegui entender seu pedido. Pode reformular?", ct);
            return;
        }

        switch (resultado.Tipo)
        {
            case "fora_contexto":
                var clientesTexto = _preFlightOptions.ClientesPermitidos.Count > 0
                    ? string.Join(", ", _preFlightOptions.ClientesPermitidos)
                    : "os clientes configurados";
                var msgFora = resultado.Motivo switch
                {
                    RouterResultado.Motivos.ClienteNaoPermitido =>
                        $"Atendo apenas pedidos de email marketing para {clientesTexto}. Esse cliente esta fora do meu escopo.",
                    RouterResultado.Motivos.CanalNaoPermitido =>
                        "Atendo apenas email marketing. Esse canal ainda nao e suportado.",
                    _ =>
                        $"Esse assunto esta fora do meu escopo. Atendo apenas pedidos de email marketing para {clientesTexto}."
                };
                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, msgFora, ct);
                return;

            case "conversa":
                var resposta = resultado.Resposta ?? "";
                if (string.IsNullOrEmpty(resposta))
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao entendi. Pode reformular?", ct);
                    return;
                }
                try
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, resposta, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao editar resposta da conversa (chat {ChatId}), enviando como nova mensagem", chatId);
                    await EnviarMensagemLongaAsync(chatId, resposta, ct);
                }
                return;

            case "onboarding":
                var pergunta = _onboarding.Iniciar(chatId, resultado.Cliente ?? "", mensagemOriginal, resultado.CamposExtraidos, resultado.Brief);
                if (pergunta == null)
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao consegui iniciar o onboarding. Tente novamente.", ct);
                    return;
                }

                await EnviarPerguntaOnboardingAsync(chatId, pergunta, ct);
                return;

            case "producao":
                if (resultado.Brief == null)
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao consegui montar o briefing. Pode reformular?", ct);
                    return;
                }

                if (!resultado.Brief.Canal.Equals("email", StringComparison.OrdinalIgnoreCase))
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, $"O canal '{resultado.Brief.Canal}' ainda nao e suportado. Atualmente so suportamos email marketing.", ct);
                    return;
                }

                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "🚀 Produzindo...", ct);

                if (_pipelineLimiter!.CurrentCount == 0)
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "🚀 Produzindo... (aguardando vaga)", ct);

                await _pipelineLimiter!.WaitAsync(ct);
                (PipelineContext Contexto, ResultadoPipeline Resultado) execucao;
                try
                {
                    execucao = await ExecutarPipelineEmail(chatId, resultado, mensagemOriginal, ct);
                }
                finally
                {
                    _pipelineLimiter.Release();
                }

                var resultadoPipeline = execucao.Resultado;
                if (!resultadoPipeline.QaAprovado)
                {
                    var feedbackMsg = "⚠️ QA nao aprovou o email apos refacoes.";
                    try
                    {
                        await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, feedbackMsg, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Falha ao editar feedback QA (chat {ChatId}), enviando como nova mensagem", chatId);
                        await _gateway!.SendMessageAsync(chatId, feedbackMsg, ct);
                    }
                    return;
                }

                await EntregarAsync(chatId, execucao.Contexto, resultadoPipeline, ct);
                return;
        }
    }

    private async Task<(PipelineContext Contexto, ResultadoPipeline Resultado)> ExecutarPipelineEmail(
        long chatId,
        RouterResultado resultado,
        string mensagemOriginal,
        CancellationToken ct)
    {
        var pipeline = _serviceProvider.GetRequiredService<PipelineEmail>();
        var runner = _serviceProvider.GetRequiredService<PipelineRunner>();
        var steps = pipeline.CriarSteps();

        var context = new PipelineContext
        {
            ChatId = chatId,
            Brief = resultado.Brief!,
            MensagemOriginal = mensagemOriginal,
            Cliente = resultado.Cliente
        };

        var resultadoPipeline = await runner.ExecutarAsync(context, steps, maxRefacoesQa: _ajustesOptions.MaxRefacoesQa, ct);
        return (context, resultadoPipeline);
    }

    internal async Task IniciarOnboardingAsync(long chatId, string cliente, string mensagemOriginal, CancellationToken ct)
    {
        var pergunta = _onboarding.Iniciar(chatId, cliente, mensagemOriginal);
        if (pergunta != null)
            await EnviarPerguntaOnboardingAsync(chatId, pergunta, ct);
    }

    private async Task HandlePhoto(Message message, CancellationToken ct)
    {
        var onboardingAtivo = _onboardingStore.Obter(message.Chat.Id);
        if (onboardingAtivo != null && onboardingAtivo.PassoAtual == OnboardingPasso.Materiais)
        {
            await HandleFotoOnboarding(message, ct);
            return;
        }

        await _gateway!.SendChatActionAsync(message.Chat.Id, ct);

        try
        {
            var photo = message.Photo!.OrderByDescending(p => p.FileSize).First();
            var imagemBytes = await _gateway!.DownloadFileAsync(photo.FileId, ct);

            var contexto = message.Caption;
            var resposta = await _analisadorImagem.DescreverImagemAsync(
                imagemBytes,
                contexto,
                ct);

            await EnviarMensagemLongaAsync(message.Chat.Id, resposta, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar foto");
            await _gateway!.SendMessageAsync(message.Chat.Id, "Erro ao analisar a imagem. Tente novamente.", ct);
        }
    }

    private async Task HandleFotoOnboarding(Message message, CancellationToken ct)
    {
        try
        {
            var photo = message.Photo!.OrderByDescending(p => p.FileSize).First();
            var imagemBytes = await _gateway!.DownloadFileAsync(photo.FileId, ct);

            _onboarding.AdicionarMaterialFoto(message.Chat.Id, imagemBytes, $"material_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg", message.Caption);
            await _gateway!.SendMessageAsync(message.Chat.Id, "📷 Foto adicionada. Envie mais ou clique em Pular.", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao adicionar foto no onboarding");
            await _gateway!.SendMessageAsync(message.Chat.Id, "Erro ao processar a foto. Tente novamente.", ct);
        }
    }

    private async Task HandleCallbackQuery(CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id;
        if (chatId == null || string.IsNullOrEmpty(callback.Data)) return;

        try
        {
            await _gateway!.AnswerCallbackQueryAsync(callback.Id!, ct);

            if (AjusteCallbacks.EhCallback(callback.Data, out var acaoAjuste))
            {
                await HandleCallbackEntregaAsync(chatId.Value, callback.Message!.MessageId, acaoAjuste, ct);
                return;
            }

            if (OnboardingCallbacks.EhAcaoEspecial(callback.Data, out var acao) && acao == "confirmar")
            {
                await ConfirmarOnboardingAsync(chatId.Value, callback.Message!.MessageId, ct);
                return;
            }

            var pergunta = _onboarding.Responder(chatId.Value, callback.Data);
            if (pergunta == null)
            {
                await _gateway!.SendMessageAsync(chatId.Value, "Onboarding expirado ou inválido. Envie um novo pedido para recomeçar.", ct);
                return;
            }

            if (OnboardingCallbacks.EhAcaoEspecial(callback.Data, out var a) && a == "recomecar")
            {
                await _gateway!.EditMessageReplyMarkupAsync(chatId.Value, callback.Message!.MessageId, ct);
                await EnviarPerguntaOnboardingAsync(chatId.Value, pergunta, ct);
                return;
            }

            await _gateway!.EditMessageTextAsync(chatId.Value, callback.Message!.MessageId, pergunta.Texto, CriarKeyboard(pergunta), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro processando callback query {CallbackId}", callback.Id);
        }
    }

    private async Task ConfirmarOnboardingAsync(long chatId, int messageId, CancellationToken ct)
    {
        var state = _onboardingStore.Obter(chatId);
        var cliente = state?.Cliente ?? "";
        var mensagemOriginal = state?.MensagemOriginal ?? "";

        var brief = _onboarding.Confirmar(chatId);
        if (brief == null)
        {
            await _gateway!.EditMessageTextAsync(chatId, messageId, "Onboarding expirado. Envie um novo pedido.", ct);
            return;
        }

        await _gateway!.EditMessageTextAsync(chatId, messageId, "🚀 Produzindo...", ct);

        if (_pipelineLimiter!.CurrentCount == 0)
            await _gateway!.EditMessageTextAsync(chatId, messageId, "🚀 Produzindo... (aguardando vaga)", ct);

        await _pipelineLimiter!.WaitAsync(ct);
        (PipelineContext Contexto, ResultadoPipeline Resultado) execucao;
        try
        {
            execucao = await ExecutarPipelineEmailOnboarding(chatId, brief, cliente, mensagemOriginal, ct);
        }
        finally
        {
            _pipelineLimiter.Release();
        }

        var resultadoPipeline = execucao.Resultado;
        if (!resultadoPipeline.QaAprovado)
        {
            var feedbackMsg = "⚠️ QA nao aprovou o email apos refacoes.";
            try
            {
                await _gateway!.EditMessageTextAsync(chatId, messageId, feedbackMsg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao editar feedback QA (chat {ChatId}), enviando como nova mensagem", chatId);
                await _gateway!.SendMessageAsync(chatId, feedbackMsg, ct);
            }
            return;
        }

        await EntregarAsync(chatId, execucao.Contexto, resultadoPipeline, ct);
    }

    internal virtual async Task<(PipelineContext Contexto, ResultadoPipeline Resultado)> ExecutarPipelineEmailOnboarding(
        long chatId,
        Brief brief,
        string cliente,
        string mensagemOriginal,
        CancellationToken ct)
    {
        var pipeline = _serviceProvider.GetRequiredService<PipelineEmail>();
        var runner = _serviceProvider.GetRequiredService<PipelineRunner>();
        var steps = pipeline.CriarSteps();

        var context = new PipelineContext
        {
            ChatId = chatId,
            Brief = brief,
            MensagemOriginal = mensagemOriginal,
            Cliente = cliente
        };

        var resultadoPipeline = await runner.ExecutarAsync(context, steps, maxRefacoesQa: _ajustesOptions.MaxRefacoesQa, ct);
        return (context, resultadoPipeline);
    }

    private async Task EnviarPerguntaOnboardingAsync(long chatId, PerguntaOnboarding pergunta, CancellationToken ct)
    {
        await _gateway!.SendMessageAsync(chatId, pergunta.Texto, CriarKeyboard(pergunta), ct);
    }

    private static InlineKeyboardMarkup? CriarKeyboard(PerguntaOnboarding pergunta)
    {
        if (pergunta.Opcoes.Count == 0) return null;

        var botoes = pergunta.Opcoes
            .Select(o => InlineKeyboardButton.WithCallbackData(o.Label, o.CallbackData))
            .ToArray();

        return new InlineKeyboardMarkup(botoes.Select(b => new[] { b }));
    }

    private static string? DetectarLink(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var trimmed = texto.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return null;
    }

    private async Task EnviarMensagemLongaAsync(long chatId, string texto, CancellationToken ct)
    {
        var partes = TelegramMessageSplitter.Dividir(texto);
        foreach (var parte in partes)
        {
            await _gateway!.SendMessageAsync(chatId, parte, ct);
        }
    }

    private async Task EntregarAsync(
        long chatId,
        PipelineContext contexto,
        ResultadoPipeline resultado,
        CancellationToken ct)
    {
        contexto.Resultado = resultado;

        var entrega = new EstadoEntrega
        {
            Contexto = contexto,
            Versao = 1,
            Fase = FaseAjuste.AposEntrega
        };
        _entregas.Guardar(chatId, entrega);

        await EnviarHtmlZipAsync(chatId, resultado, entrega.Versao, ct);
        await EnviarMenuEntregaAsync(chatId, entrega.Versao, ct);
    }

    private async Task EnviarMenuEntregaAsync(long chatId, int versao, CancellationToken ct)
    {
        var texto =
            $"✅ Email entregue (v{versao}).\n\n" +
            "Quer aprovar ou pedir ajustes pontuais?\n" +
            "Se pedir ajustes, eu pergunto o que não ficou legal e refaço só a etapa necessária.";
        await _gateway!.SendMessageAsync(chatId, texto, CriarKeyboardEntrega(), ct);
    }

    private static InlineKeyboardMarkup CriarKeyboardEntrega()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("✅ Aprovar", AjusteCallbacks.AprovarEntrega) },
            new[] { InlineKeyboardButton.WithCallbackData("🔧 Pedir ajustes", AjusteCallbacks.PedirAjustes) }
        });
    }

    private static InlineKeyboardMarkup CriarKeyboardPlano()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("✅ Aplicar ajuste", AjusteCallbacks.AplicarAjuste) },
            new[] { InlineKeyboardButton.WithCallbackData("✋ Cancelar", AjusteCallbacks.CancelarAjuste) }
        });
    }

    private async Task HandleCallbackEntregaAsync(long chatId, int messageId, string acao, CancellationToken ct)
    {
        if (acao == AjusteCallbacks.AprovarEntrega)
        {
            await AprovarEntregaAsync(chatId, messageId, ct);
            return;
        }

        if (acao == AjusteCallbacks.PedirAjustes)
        {
            await IniciarColetaFeedbackAsync(chatId, messageId, ct);
            return;
        }

        if (acao == AjusteCallbacks.AplicarAjuste)
        {
            await AplicarAjusteAsync(chatId, messageId, ct);
            return;
        }

        if (acao == AjusteCallbacks.CancelarAjuste)
        {
            await CancelarAjusteAsync(chatId, messageId, ct);
            return;
        }

        _logger.LogWarning("Callback de entrega desconhecido: {Callback}", acao);
    }

    private async Task AprovarEntregaAsync(long chatId, int messageId, CancellationToken ct)
    {
        var entrega = _entregas.Obter(chatId);
        if (entrega == null)
        {
            await EditarOuEnviarAsync(chatId, messageId,
                "Não encontrei uma entrega recente para aprovar. Envie um novo pedido.", null, ct);
            return;
        }

        entrega.Aprovada = true;
        entrega.Fase = FaseAjuste.AposEntrega;
        entrega.Plano = null;
        _entregas.Guardar(chatId, entrega);

        await EditarOuEnviarAsync(chatId, messageId,
            $"✅ Entrega v{entrega.Versao} aprovada! Se quiser um novo email, é só enviar um novo pedido.",
            null, ct);
    }

    private async Task IniciarColetaFeedbackAsync(long chatId, int messageId, CancellationToken ct)
    {
        var entrega = _entregas.Obter(chatId);
        if (entrega == null)
        {
            await EditarOuEnviarAsync(chatId, messageId,
                "Não encontrei uma entrega recente para ajustar. Envie um novo pedido.", null, ct);
            return;
        }

        if (entrega.RodadasAjuste >= _ajustesOptions.MaxRodadas)
        {
            await EditarOuEnviarAsync(chatId, messageId,
                $"Esta entrega já teve {entrega.RodadasAjuste} rodada(s) de ajuste, o limite configurado. " +
                "Aprove a versão atual ou envie um novo pedido.", null, ct);
            return;
        }

        entrega.Fase = FaseAjuste.AguardandoFeedback;
        entrega.Plano = null;
        entrega.Aprovada = false;
        _entregas.Guardar(chatId, entrega);

        await EditarOuEnviarAsync(chatId, messageId,
            $"🔧 Certo! Descreva em uma mensagem o que não ficou legal no email v{entrega.Versao}.\n" +
            "Diga o que deve mudar e onde (ex.: assunto, textos, cores, layout, imagem).\n\n" +
            "A versão atual continua valendo até você confirmar o ajuste.",
            null, ct);
    }

    private async Task ProcessarFeedbackAjusteAsync(long chatId, string texto, CancellationToken ct)
    {
        var entrega = _entregas.Obter(chatId);
        if (entrega == null || entrega.Fase != FaseAjuste.AguardandoFeedback)
            return;

        int mensagemProgressoId;
        try
        {
            var mensagem = await _gateway!.SendMessageAsync(chatId, "🧠 Analisando o ajuste...", ct);
            mensagemProgressoId = mensagem.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar mensagem de progresso do ajuste (chat {ChatId})", chatId);
            return;
        }

        try
        {
            var plano = await _refinador.ClassificarAsync(entrega.Contexto, texto, ct);

            if (plano == null)
            {
                await EditarOuEnviarAsync(chatId, mensagemProgressoId,
                    "Não consegui identificar o que ajustar. Descreva com mais detalhe o que deve mudar " +
                    "e onde (ex.: \"o título está muito formal\", \"a imagem não tem a ver com o tema\").",
                    null, ct);
                return;
            }

            entrega.Plano = plano;
            entrega.Fase = FaseAjuste.AguardandoConfirmacao;
            _entregas.Guardar(chatId, entrega);

            var mensagemPlano =
                $"🔧 Ajuste identificado — etapa: {RotuloEtapa(plano.StepAlvo)}\n\n" +
                $"{plano.Instrucoes}\n\n" +
                $"Posso aplicar? A nova versão substitui a entrega v{entrega.Versao}.";
            await EditarOuEnviarAsync(chatId, mensagemProgressoId, mensagemPlano, CriarKeyboardPlano(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao classificar ajuste (chat {ChatId})", chatId);
            await NotificarErroAsync(chatId, mensagemProgressoId, ct);
        }
    }

    private async Task CancelarAjusteAsync(long chatId, int messageId, CancellationToken ct)
    {
        var entrega = _entregas.Obter(chatId);
        if (entrega == null)
        {
            await EditarOuEnviarAsync(chatId, messageId,
                "Não encontrei uma entrega recente. Envie um novo pedido.", null, ct);
            return;
        }

        entrega.Plano = null;
        entrega.Fase = FaseAjuste.AposEntrega;
        _entregas.Guardar(chatId, entrega);

        await EditarOuEnviarAsync(chatId, messageId,
            $"✋ Ajuste cancelado. A entrega v{entrega.Versao} continua valendo.",
            CriarKeyboardEntrega(), ct);
    }

    private async Task AplicarAjusteAsync(long chatId, int messageId, CancellationToken ct)
    {
        var entrega = _entregas.Obter(chatId);
        if (entrega?.Plano == null || entrega.Fase != FaseAjuste.AguardandoConfirmacao)
        {
            await EditarOuEnviarAsync(chatId, messageId,
                "Esse ajuste expirou. Clique em Pedir ajustes para recomeçar.", null, ct);
            return;
        }

        var plano = entrega.Plano;
        await EditarOuEnviarAsync(chatId, messageId,
            $"🛠️ Aplicando ajuste em {RotuloEtapa(plano.StepAlvo)}...", null, ct);

        PipelineContext clone;
        ResultadoPipeline resultado;
        await _pipelineLimiter!.WaitAsync(ct);
        try
        {
            var pipeline = _serviceProvider.GetRequiredService<PipelineEmail>();
            var runner = _serviceProvider.GetRequiredService<PipelineRunner>();

            clone = PipelineContextCloner.Clonar(entrega.Contexto);
            clone.FeedbackAjuste =
                $"Pedido do cliente: {plano.FeedbackCliente}\nInstrucoes: {plano.Instrucoes}";
            if (plano.StepAlvo.Equals("template", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(plano.TemplateId))
            {
                clone.TemplateId = plano.TemplateId;
            }

            resultado = await runner.ExecutarAjusteAsync(
                clone,
                pipeline.CriarSteps(),
                plano.StepAlvo,
                _ajustesOptions.MaxRefacoesQa,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar ajuste (chat {ChatId})", chatId);
            entrega.Fase = FaseAjuste.AposEntrega;
            entrega.Plano = null;
            _entregas.Guardar(chatId, entrega);
            await EditarOuEnviarAsync(chatId, messageId,
                "Desculpe, ocorreu um erro ao aplicar o ajuste. A versão anterior continua valendo.",
                CriarKeyboardEntrega(), ct);
            return;
        }
        finally
        {
            _pipelineLimiter.Release();
        }

        if (!resultado.QaAprovado)
        {
            entrega.Fase = FaseAjuste.AposEntrega;
            entrega.Plano = null;
            _entregas.Guardar(chatId, entrega);

            var feedbackMsg =
                $"⚠️ O ajuste não passou na revisão de qualidade. A entrega v{entrega.Versao} continua valendo.";
            await EditarOuEnviarAsync(chatId, messageId, feedbackMsg, CriarKeyboardEntrega(), ct);
            return;
        }

        clone.Resultado = resultado;
        entrega.Contexto = clone;
        entrega.Versao++;
        entrega.RodadasAjuste++;
        entrega.Fase = FaseAjuste.AposEntrega;
        entrega.Plano = null;
        entrega.Aprovada = false;
        _entregas.Guardar(chatId, entrega);

        await EnviarHtmlZipAsync(chatId, resultado, entrega.Versao, ct);
        await EnviarMenuEntregaAsync(chatId, entrega.Versao, ct);
    }

    private async Task EditarOuEnviarAsync(
        long chatId,
        int messageId,
        string texto,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        try
        {
            await _gateway!.EditMessageTextAsync(chatId, messageId, texto, keyboard, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao editar mensagem (chat {ChatId}), enviando como nova mensagem", chatId);
            await _gateway!.SendMessageAsync(chatId, texto, keyboard, ct);
        }
    }

    private static string RotuloEtapa(string stepAlvo)
    {
        return stepAlvo switch
        {
            "copy" => "textos (copy)",
            "diagramacao" => "layout do corpo (diagramação)",
            "hero" => "imagem principal (hero)",
            "template" => "template do email",
            _ => stepAlvo
        };
    }

    private async Task EnviarHtmlZipAsync(long chatId, ResultadoPipeline resultado, int versao, CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var htmlEntry = archive.CreateEntry("entregavel.html");
            using (var entryStream = htmlEntry.Open())
            using (var writer = new StreamWriter(entryStream))
            {
                await writer.WriteAsync(resultado.RespostaFinal);
            }

            var imageIndex = 0;
            foreach (var imagem in resultado.Imagens)
            {
                imageIndex++;
                var ext = ImageFormatHelper.DetectarExtensao(imagem.Bytes);
                var imgEntry = archive.CreateEntry($"imagens/gerada_{imageIndex}{ext}");
                using var entryStream = imgEntry.Open();
                await entryStream.WriteAsync(imagem.Bytes, ct);
            }

            var assetIndex = 0;
            foreach (var asset in resultado.AssetsAnexados)
            {
                assetIndex++;
                var ext = ImageFormatHelper.DetectarExtensao(asset.Bytes);
                var nomeSeguro = (asset.Legenda ?? $"asset_{assetIndex}")
                    .Replace("/", "_").Replace("\\", "_").Replace(" ", "_");
                var nomeSemExt = Path.GetFileNameWithoutExtension(nomeSeguro);
                var assetEntry = archive.CreateEntry($"assets/{nomeSemExt}{ext}");
                using var entryStream = assetEntry.Open();
                await entryStream.WriteAsync(asset.Bytes, ct);
            }
        }

        memoryStream.Position = 0;
        await _gateway!.SendDocumentAsync(chatId, memoryStream, $"entregavel_v{versao}.zip",
            $"Entregavel HTML completo com assets (v{versao})", ct);
    }
}
