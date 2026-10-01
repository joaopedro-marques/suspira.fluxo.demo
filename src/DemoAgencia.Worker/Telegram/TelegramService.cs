using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
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
    private readonly RoteadorService _roteador;
    private readonly HistoricoChat _historico;
    private readonly StreamingService _streaming;
    private TelegramBotClient? _botClient;
    private readonly Dictionary<long, string> _agentesPorChat = new();

    public TelegramService(
        ILogger<TelegramService> logger,
        IConfiguration configuration,
        AgenteLoader agenteLoader,
        OpenRouterService openRouter,
        RoteadorService roteador,
        HistoricoChat historico,
        StreamingService streaming)
    {
        _logger = logger;
        _configuration = configuration;
        _agenteLoader = agenteLoader;
        _openRouter = openRouter;
        _roteador = roteador;
        _historico = historico;
        _streaming = streaming;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = _configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogError("Token do Telegram nao configurado. Defina TELEGRAM_BOT_TOKEN.");
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
                text: $"Comandos:\n/start - Inicia o bot\n/help - Mostra esta ajuda\n/agentes - Lista agentes disponiveis\n/imagem <prompt> - Gera uma imagem\n/limpar - Limpa historico do chat\n/reset - Deseleciona agente e limpa historico\n\nAgentes:\n{comandosAgentes}",
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

        if (command == "/imagem")
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await _botClient!.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Uso: /imagem <descrição da imagem>",
                    cancellationToken: ct);
                return;
            }

            await _botClient!.SendChatAction(message.Chat.Id, ChatAction.UploadPhoto, cancellationToken: ct);
            var imagemBytes = await _openRouter.GerarImagemAsync(args, ct);
            
            if (imagemBytes == null)
            {
                await _botClient!.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Erro ao gerar imagem. Tente novamente.",
                    cancellationToken: ct);
                return;
            }

            using var stream = new MemoryStream(imagemBytes);
            await _botClient!.SendPhoto(
                chatId: message.Chat.Id,
                photo: stream,
                caption: args,
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
                    text: $"Agente {agente.Nome} selecionado. Envie sua mensagem para interagir.",
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
        await _botClient!.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        string? comandoAtivo = null;
        if (_agentesPorChat.TryGetValue(message.Chat.Id, out var cmd))
        {
            comandoAtivo = cmd;
        }

        var (modelo, persona) = await _roteador.RoteearAsync(
            message.Chat.Id,
            text,
            comandoAtivo,
            ct);

        await EnviarComStreaming(message.Chat.Id, async () =>
        {
            return _openRouter.CompletarStreamingAsync(
                message.Chat.Id,
                text,
                persona,
                modelo,
                _historico,
                ct);
        }, ct);
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
