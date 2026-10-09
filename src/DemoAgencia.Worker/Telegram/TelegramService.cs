using System.IO.Compression;
using System.Text;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
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
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly RateLimiterService _rateLimiter;
    private readonly ITelegramGatewayFactory _gatewayFactory;
    private readonly RouterService _router;
    private readonly ConversaPendenteStore _pendencias;
    private readonly OnboardingService _onboarding;
    private readonly OnboardingStore _onboardingStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UpdateDispatcher> _dispatcherLogger;
    private ITelegramGateway? _gateway;
    private UpdateDispatcher? _dispatcher;
    private SemaphoreSlim? _pipelineLimiter;

    public TelegramService(
        ILogger<TelegramService> logger,
        IOptions<TelegramOptions> options,
        IOptions<ConcorrenciaOptions> concorrencia,
        IAnalisadorImagem analisadorImagem,
        RateLimiterService rateLimiter,
        ITelegramGatewayFactory gatewayFactory,
        RouterService router,
        ConversaPendenteStore pendencias,
        OnboardingService onboarding,
        OnboardingStore onboardingStore,
        IServiceProvider serviceProvider,
        ILogger<UpdateDispatcher> dispatcherLogger)
    {
        _logger = logger;
        _options = options.Value;
        _concorrencia = concorrencia.Value;
        _analisadorImagem = analisadorImagem;
        _rateLimiter = rateLimiter;
        _gatewayFactory = gatewayFactory;
        _router = router;
        _pendencias = pendencias;
        _onboarding = onboarding;
        _onboardingStore = onboardingStore;
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

                    var chatId = update.Message?.Chat.Id;
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
            var help = """
                Comandos:
                /start - Inicia o bot
                /help - Mostra esta ajuda

                Envie uma mensagem livre para criar um email marketing para o cliente MRV.
                Exemplo: "Crie um email para a MRV sobre pos-compra"
                """;
            await _gateway!.SendMessageAsync(message.Chat.Id, help, ct);
            return;
        }

        _pendencias.Remover(message.Chat.Id);
        await _gateway!.SendMessageAsync(message.Chat.Id, "Comando nao reconhecido. Use /help para ver os comandos disponiveis.", ct);
    }

    private async Task HandleTextMessage(Message message, string text, CancellationToken ct)
    {
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
            var textoInicial = pendente != null ? "🧠 Retomando com suas respostas..." : "🧠 Analisando seu pedido...";
            var msg = await _gateway!.SendMessageAsync(message.Chat.Id, textoInicial, ct);
            mensagemProgressoId = msg.MessageId;

            resultadoRouter = pendente != null
                ? await _router.ResumirAsync(message.Chat.Id, text, ct)
                : await _router.IniciarAsync(message.Chat.Id, text, ct);

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

        if (estado.PassoAtual == OnboardingPasso.CtaLink)
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
                var msgFora = resultado.Motivo switch
                {
                    RouterResultado.Motivos.ClienteNaoPermitido =>
                        "Atendo apenas pedidos de email marketing para o cliente MRV. Esse cliente esta fora do meu escopo.",
                    RouterResultado.Motivos.CanalNaoPermitido =>
                        "Atendo apenas email marketing. Esse canal ainda nao e suportado.",
                    _ =>
                        "Esse assunto esta fora do meu escopo. Atendo apenas pedidos de email marketing para o cliente MRV."
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
                if (string.IsNullOrEmpty(resultado.Cliente))
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao consegui identificar o cliente. Pode informar?", ct);
                    return;
                }

                var pergunta = _onboarding.Iniciar(chatId, resultado.Cliente, mensagemOriginal, resultado.CamposExtraidos);
                if (pergunta == null)
                {
                    await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Nao consegui iniciar o onboarding. Tente novamente.", ct);
                    return;
                }

                await _gateway!.EditMessageReplyMarkupAsync(chatId, mensagemProgressoId, ct);
                await EnviarPerguntaOnboardingAsync(chatId, pergunta, ct);
                return;

            case "esclarecimento":
                var perguntas = string.Join("\n", resultado.Perguntas.Select((p, i) => $"{i + 1}. {p}"));
                var textoPerguntas = $"Preciso de mais alguns detalhes:\n{perguntas}";
                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, textoPerguntas, ct);
                return;

            case "limite_perguntas":
                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId,
                    "Nao consegui reunir todos os detalhes mesmo com as perguntas. Vamos recomecar: reenvie o pedido completo com cliente, objetivo, publico, oferta e canal em uma unica mensagem.", ct);
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
                ResultadoPipeline resultadoPipeline;
                try
                {
                    resultadoPipeline = await ExecutarPipelineEmail(chatId, resultado, mensagemOriginal, ct);
                }
                finally
                {
                    _pipelineLimiter.Release();
                }

                if (!resultadoPipeline.QaAprovado)
                {
                    var feedbackMsg = $"⚠️ QA nao aprovou o email apos refacoes.";
                    if (!string.IsNullOrEmpty(resultadoPipeline.QaFeedbackFinal))
                        feedbackMsg += $"\n{resultadoPipeline.QaFeedbackFinal}";
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

                await EnviarHtmlZipAsync(chatId, resultadoPipeline, ct);
                return;
        }
    }

    private async Task<ResultadoPipeline> ExecutarPipelineEmail(long chatId, RouterResultado resultado, string mensagemOriginal, CancellationToken ct)
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

        return await runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, ct);
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
        ResultadoPipeline resultadoPipeline;
        try
        {
            resultadoPipeline = await ExecutarPipelineEmailOnboarding(chatId, brief, ct);
        }
        finally
        {
            _pipelineLimiter.Release();
        }

        if (!resultadoPipeline.QaAprovado)
        {
            var feedbackMsg = "⚠️ QA nao aprovou o email apos refacoes.";
            if (!string.IsNullOrEmpty(resultadoPipeline.QaFeedbackFinal))
                feedbackMsg += $"\n{resultadoPipeline.QaFeedbackFinal}";
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

        await EnviarHtmlZipAsync(chatId, resultadoPipeline, ct);
    }

    private async Task<ResultadoPipeline> ExecutarPipelineEmailOnboarding(long chatId, Brief brief, CancellationToken ct)
    {
        var pipeline = _serviceProvider.GetRequiredService<PipelineEmail>();
        var runner = _serviceProvider.GetRequiredService<PipelineRunner>();
        var steps = pipeline.CriarSteps();

        var onboardingState = _onboardingStore.Obter(chatId);
        var context = new PipelineContext
        {
            ChatId = chatId,
            Brief = brief,
            MensagemOriginal = onboardingState?.MensagemOriginal ?? "",
            Cliente = onboardingState?.Cliente
        };

        return await runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, ct);
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

    private async Task EnviarHtmlZipAsync(long chatId, ResultadoPipeline resultado, CancellationToken ct)
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
                var ext = DetectarExtensaoImagem(imagem.Bytes);
                var imgEntry = archive.CreateEntry($"imagens/gerada_{imageIndex}{ext}");
                using var entryStream = imgEntry.Open();
                await entryStream.WriteAsync(imagem.Bytes, ct);
            }

            var assetIndex = 0;
            foreach (var asset in resultado.AssetsAnexados)
            {
                assetIndex++;
                var ext = DetectarExtensaoImagem(asset.Bytes);
                var nomeSeguro = (asset.Legenda ?? $"asset_{assetIndex}")
                    .Replace("/", "_").Replace("\\", "_").Replace(" ", "_");
                var nomeSemExt = Path.GetFileNameWithoutExtension(nomeSeguro);
                var assetEntry = archive.CreateEntry($"assets/{nomeSemExt}{ext}");
                using var entryStream = assetEntry.Open();
                await entryStream.WriteAsync(asset.Bytes, ct);
            }
        }

        memoryStream.Position = 0;
        await _gateway!.SendDocumentAsync(chatId, memoryStream, "entregavel.zip", "Entregavel HTML completo com assets", ct);
    }

    private static string DetectarExtensaoImagem(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return ".jpg";
        if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ".gif";
        if (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return ".webp";
        return ".bin";
    }
}
