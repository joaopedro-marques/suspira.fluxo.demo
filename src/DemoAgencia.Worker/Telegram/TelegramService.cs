using System.IO.Compression;
using System.Text;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Seguranca;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace DemoAgencia.Worker.Telegram;

public class TelegramService : BackgroundService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly TelegramOptions _options;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly RateLimiterService _rateLimiter;
    private readonly ITelegramGatewayFactory _gatewayFactory;
    private readonly RouterService _router;
    private readonly ConversaPendenteStore _pendencias;
    private readonly IServiceProvider _serviceProvider;
    private ITelegramGateway? _gateway;

    public TelegramService(
        ILogger<TelegramService> logger,
        IOptions<TelegramOptions> options,
        IAnalisadorImagem analisadorImagem,
        RateLimiterService rateLimiter,
        ITelegramGatewayFactory gatewayFactory,
        RouterService router,
        ConversaPendenteStore pendencias,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _analisadorImagem = analisadorImagem;
        _rateLimiter = rateLimiter;
        _gatewayFactory = gatewayFactory;
        _router = router;
        _pendencias = pendencias;
        _serviceProvider = serviceProvider;
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
        var pendente = _pendencias.Obter(message.Chat.Id);
        RouterResultado? resultadoRouter;

        if (pendente != null)
        {
            var msg = await _gateway!.SendMessageAsync(message.Chat.Id, "🧠 Retomando com suas respostas...", ct);
            resultadoRouter = await _router.ResumirAsync(message.Chat.Id, text, ct);
            await ProcessarResultadoRouter(message.Chat.Id, resultadoRouter, msg.MessageId, text, ct);
            return;
        }

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

            if (mensagemProgressoId != null)
            {
                try
                {
                    await _gateway!.EditMessageTextAsync(message.Chat.Id, mensagemProgressoId.Value, "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.", ct);
                }
                catch
                {
                    await _gateway!.SendMessageAsync(message.Chat.Id, "Desculpe, ocorreu um erro ao processar sua mensagem. Tente novamente.", ct);
                }
            }
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
                catch
                {
                    await EnviarMensagemLongaAsync(chatId, resposta, ct);
                }
                return;

            case "esclarecimento":
                var perguntas = string.Join("\n", resultado.Perguntas.Select((p, i) => $"{i + 1}. {p}"));
                var textoPerguntas = $"Preciso de mais alguns detalhes:\n{perguntas}";
                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, textoPerguntas, ct);
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

                var resultadoPipeline = await ExecutarPipelineEmail(chatId, resultado, mensagemOriginal, ct);

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

    private async Task HandlePhoto(Message message, CancellationToken ct)
    {
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
                var assetEntry = archive.CreateEntry($"assets/{nomeSeguro}{ext}");
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
