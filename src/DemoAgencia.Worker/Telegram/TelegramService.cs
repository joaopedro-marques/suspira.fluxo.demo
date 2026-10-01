using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Seguranca;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DemoAgencia.Worker.Telegram;

public class TelegramService : BackgroundService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AgenteLoader _agenteLoader;
    private readonly OpenRouterService _openRouter;
    private readonly PipelineService _pipeline;
    private readonly HistoricoChat _historico;
    private readonly StreamingService _streaming;
    private readonly RateLimiterService _rateLimiter;
    private readonly AnonimizadorService _anonimizador;
    private TelegramBotClient? _botClient;
    private readonly Dictionary<long, string> _agentesPorChat = new();
    private bool _allowlistAvisada = false;

    public TelegramService(
        ILogger<TelegramService> logger,
        IConfiguration configuration,
        AgenteLoader agenteLoader,
        OpenRouterService openRouter,
        PipelineService pipeline,
        HistoricoChat historico,
        StreamingService streaming,
        RateLimiterService rateLimiter,
        AnonimizadorService anonimizador)
    {
        _logger = logger;
        _configuration = configuration;
        _agenteLoader = agenteLoader;
        _openRouter = openRouter;
        _pipeline = pipeline;
        _historico = historico;
        _streaming = streaming;
        _rateLimiter = rateLimiter;
        _anonimizador = anonimizador;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = _configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogError("Token do Telegram nao configurado. Defina Telegram__BotToken.");
            return;
        }

        _botClient = new TelegramBotClient(botToken);

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
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
                var updates = await _botClient.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    await ProcessUpdate(update, stoppingToken);
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

    private async Task ProcessUpdate(Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } message)
            {
                if (!ChatPermitido(message.Chat.Id))
                {
                    if (!_allowlistAvisada)
                    {
                        _logger.LogWarning("Chat {ChatId} nao esta na allowlist. Configure Telegram__ChatIdsPermitidos.", message.Chat.Id);
                        _allowlistAvisada = true;
                    }
                    await _botClient!.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Acesso restrito. Este bot esta configurado para chats especificos.",
                        cancellationToken: ct);
                    return;
                }

                if (!_rateLimiter.PodeProcessar(message.Chat.Id))
                {
                    await _botClient!.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Voce esta enviando mensagens muito rapido. Aguarde um momento.",
                        cancellationToken: ct);
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

    private bool ChatPermitido(long chatId)
    {
        var allowlistConfig = _configuration["Telegram:ChatIdsPermitidos"];
        
        if (string.IsNullOrWhiteSpace(allowlistConfig))
        {
            return true;
        }

        var idsPermitidos = allowlistConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => long.TryParse(id, out _))
            .Select(long.Parse)
            .ToHashSet();

        return idsPermitidos.Contains(chatId);
    }

    private async Task HandleCommand(Message message, CancellationToken ct)
    {
        var parts = message.Text!.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : string.Empty;

        if (command == "/start")
        {
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: "Bem-vindo! Sou o DemoAgencia Bot. Use /help para ver os comandos disponiveis.",
                cancellationToken: ct);
            return;
        }

        if (command == "/help")
        {
            var agentes = _agenteLoader.ListarAgentes();
            var comandosAgentes = string.Join("\n", agentes.SelectMany(a => a.Comandos.Select(c => $"{c} - {a.Nome}: {a.Descricao}")));
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: $"Comandos:\n/start - Inicia o bot\n/help - Mostra esta ajuda\n/agentes - Lista agentes disponiveis\n/limpar - Limpa historico do chat\n/reset - Deseleciona agente e limpa historico\n\nMensagens livres sao processadas pelo pipeline multi-agente.\n\nAgentes (atalhos diretos):\n{comandosAgentes}",
                cancellationToken: ct);
            return;
        }

        if (command == "/agentes")
        {
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: await GetAgentsList(),
                cancellationToken: ct);
            return;
        }

        if (command == "/limpar")
        {
            _historico.LimparHistorico(message.Chat.Id);
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: "Historico limpo.",
                cancellationToken: ct);
            return;
        }

        if (command == "/reset")
        {
            _agentesPorChat.Remove(message.Chat.Id);
            _historico.LimparHistorico(message.Chat.Id);
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: "Agente deselecionado e historico limpo.",
                cancellationToken: ct);
            return;
        }

        var agente = _agenteLoader.ObterPorComando(command);
        if (agente != null)
        {
            if (string.IsNullOrEmpty(args))
            {
                _agentesPorChat[message.Chat.Id] = command;
                await _botClient!.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Agente {agente.Nome} selecionado. Envie sua mensagem para interagir diretamente.",
                    cancellationToken: ct);
            }
            else
            {
                await _botClient!.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);
                
                await EnviarComStreaming(message.Chat.Id, async () =>
                {
                    return _openRouter.CompletarStreamingAsync(
                        message.Chat.Id,
                        args,
                        agente.Persona,
                        agente.ModeloAlvo,
                        _historico,
                        ct);
                }, ct);
            }
            return;
        }

        await _botClient!.SendMessage(
            chatId: message.Chat.Id,
            text: "Comando nao reconhecido. Use /help para ver os comandos disponiveis.",
            cancellationToken: ct);
    }

    private async Task HandleTextMessage(Message message, string text, CancellationToken ct)
    {
        // Se ha um agente selecionado por comando, bypass direto
        if (_agentesPorChat.TryGetValue(message.Chat.Id, out var cmd))
        {
            var agente = _agenteLoader.ObterPorComando(cmd);
            if (agente != null)
            {
                await _botClient!.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);
                
                await EnviarComStreaming(message.Chat.Id, async () =>
                {
                    return _openRouter.CompletarStreamingAsync(
                        message.Chat.Id,
                        text,
                        agente.Persona,
                        agente.ModeloAlvo,
                        _historico,
                        ct);
                }, ct);
                return;
            }
        }

        // Mensagem livre: pipeline multi-agente com progresso
        Message? mensagemProgresso = null;

        try
        {
            mensagemProgresso = await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: "🧠 Analisando seu pedido...",
                cancellationToken: ct);

            var resultado = await _pipeline.ExecutarAsync(
                message.Chat.Id,
                text,
                async (progresso) =>
                {
                    if (mensagemProgresso != null)
                    {
                        try
                        {
                            await _botClient!.EditMessageText(
                                chatId: message.Chat.Id,
                                messageId: mensagemProgresso.MessageId,
                                text: progresso,
                                cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Erro ao atualizar progresso");
                        }
                    }
                },
                ct);

            // Enviar resposta final
            if (resultado.Imagem != null)
            {
                using var stream = new MemoryStream(resultado.Imagem);
                await _botClient!.SendPhoto(
                    chatId: message.Chat.Id,
                    photo: stream,
                    caption: resultado.LegendaImagem ?? resultado.RespostaFinal,
                    cancellationToken: ct);
            }

            if (!string.IsNullOrEmpty(resultado.RespostaFinal))
            {
                if (mensagemProgresso != null && resultado.Imagem == null)
                {
                    // Editar mensagem de progresso com resposta final
                    try
                    {
                        await _botClient!.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: mensagemProgresso.MessageId,
                            text: resultado.RespostaFinal,
                            cancellationToken: ct);
                    }
                    catch
                    {
                        // Se falhar ao editar, enviar nova mensagem
                        await _botClient!.SendMessage(
                            chatId: message.Chat.Id,
                            text: resultado.RespostaFinal,
                            cancellationToken: ct);
                    }
                }
                else if (resultado.Imagem != null)
                {
                    // Se tem imagem, enviar texto como mensagem separada
                    await _botClient!.SendMessage(
                        chatId: message.Chat.Id,
                        text: resultado.RespostaFinal,
                        cancellationToken: ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar mensagem no pipeline");
            
            if (mensagemProgresso != null)
            {
                try
                {
                    await _botClient!.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: mensagemProgresso.MessageId,
                        text: "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
                        cancellationToken: ct);
                }
                catch
                {
                    await _botClient!.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.",
                        cancellationToken: ct);
                }
            }
        }
    }

    private async Task HandlePhoto(Message message, CancellationToken ct)
    {
        await _botClient!.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        try
        {
            var photo = message.Photo!.OrderByDescending(p => p.FileSize).First();
            var file = await _botClient!.GetFile(photo.FileId, ct);
            
            using var memoryStream = new MemoryStream();
            await _botClient!.GetInfoAndDownloadFile(file.FileId, memoryStream, ct);
            var imagemBytes = memoryStream.ToArray();

            var contexto = message.Caption;
            var resposta = await _openRouter.AnalisarImagemAsync(
                message.Chat.Id,
                imagemBytes,
                contexto,
                _historico,
                ct);

            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: resposta,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar foto");
            await _botClient!.SendMessage(
                chatId: message.Chat.Id,
                text: "Erro ao analisar a imagem. Tente novamente.",
                cancellationToken: ct);
        }
    }

    private async Task EnviarComStreaming(long chatId, Func<Task<IAsyncEnumerable<string>>> obterStream, CancellationToken ct)
    {
        Message? mensagemEnviada = null;

        await _streaming.ProcessarStreamingAsync(
            chatId,
            await obterStream(),
            async (texto) =>
            {
                mensagemEnviada = await _botClient!.SendMessage(
                    chatId: chatId,
                    text: texto,
                    cancellationToken: ct);
                return mensagemEnviada.MessageId;
            },
            async (messageId, texto) =>
            {
                await _botClient!.EditMessageText(
                    chatId: chatId,
                    messageId: (int)messageId,
                    text: texto,
                    cancellationToken: ct);
            },
            ct);
    }

    private Task<string> GetAgentsList()
    {
        var agentes = _agenteLoader.ListarAgentes();
        if (agentes.Count == 0)
        {
            return Task.FromResult("Nenhum agente disponivel no momento.");
        }

        var lines = agentes.Select(a => $"• {a.Nome} - {a.Descricao}\n  Comandos: {string.Join(", ", a.Comandos)}");
        return Task.FromResult("Agentes disponiveis:\n\n" + string.Join("\n\n", lines));
    }
}
