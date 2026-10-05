using System.IO.Compression;
using System.Text;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace DemoAgencia.Worker.Telegram;

public class TelegramService : BackgroundService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly TelegramOptions _options;
    private readonly IAgentesCatalogo _agenteLoader;
    private readonly IStreamingChat _openRouter;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly OrquestradorLoopService _loop;
    private readonly IHistoricoChat _historico;
    private readonly IStreamingService _streaming;
    private readonly RateLimiterService _rateLimiter;
    private readonly ITelegramGatewayFactory _gatewayFactory;
    private readonly RouterService _router;
    private readonly ConversaPendenteStore _pendencias;
    private readonly IServiceProvider _serviceProvider;
    private ITelegramGateway? _gateway;
    private readonly Dictionary<long, string> _agentesPorChat = new();

    public TelegramService(
        ILogger<TelegramService> logger,
        IOptions<TelegramOptions> options,
        IAgentesCatalogo agenteLoader,
        IStreamingChat openRouter,
        IAnalisadorImagem analisadorImagem,
        OrquestradorLoopService loop,
        IHistoricoChat historico,
        IStreamingService streaming,
        RateLimiterService rateLimiter,
        ITelegramGatewayFactory gatewayFactory,
        RouterService router,
        ConversaPendenteStore pendencias,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _agenteLoader = agenteLoader;
        _openRouter = openRouter;
        _analisadorImagem = analisadorImagem;
        _loop = loop;
        _historico = historico;
        _streaming = streaming;
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
        _pendencias.Remover(message.Chat.Id);

        var parts = message.Text!.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : string.Empty;

        if (command == "/start")
        {
            await _gateway!.SendMessageAsync(message.Chat.Id, "Bem-vindo! Sou o DemoAgencia Bot. Use /help para ver os comandos disponiveis.", ct);
            return;
        }

        if (command == "/help")
        {
            var agentes = _agenteLoader.ListarAgentes();
            var comandosAgentes = string.Join("\n", agentes.SelectMany(a => a.Comandos.Select(c => $"{c} - {a.Nome}: {a.Descricao}")));
            await _gateway!.SendMessageAsync(message.Chat.Id, $"Comandos:\n/start - Inicia o bot\n/help - Mostra esta ajuda\n/agentes - Lista agentes disponiveis\n/limpar - Limpa historico do chat\n/reset - Deseleciona agente e limpa historico\n\nMensagens livres sao processadas pelo orquestrador multi-agente.\n\nAgentes (atalhos diretos):\n{comandosAgentes}", ct);
            return;
        }

        if (command == "/agentes")
        {
            await _gateway!.SendMessageAsync(message.Chat.Id, await GetAgentsList(), ct);
            return;
        }

        if (command == "/limpar")
        {
            _historico.LimparHistorico(message.Chat.Id);
            _pendencias.Remover(message.Chat.Id);
            await _gateway!.SendMessageAsync(message.Chat.Id, "Historico limpo.", ct);
            return;
        }

        if (command == "/reset")
        {
            _agentesPorChat.Remove(message.Chat.Id);
            _historico.LimparHistorico(message.Chat.Id);
            _pendencias.Remover(message.Chat.Id);
            await _gateway!.SendMessageAsync(message.Chat.Id, "Agente deselecionado e historico limpo.", ct);
            return;
        }

        var agente = _agenteLoader.ObterPorComando(command);
        if (agente != null)
        {
            if (string.IsNullOrEmpty(args))
            {
                _agentesPorChat[message.Chat.Id] = command;
                await _gateway!.SendMessageAsync(message.Chat.Id, $"Agente {agente.Nome} selecionado. Envie sua mensagem para interagir diretamente.", ct);
            }
            else
            {
                await _gateway!.SendChatActionAsync(message.Chat.Id, ct);
                
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

        await _gateway!.SendMessageAsync(message.Chat.Id, "Comando nao reconhecido. Use /help para ver os comandos disponiveis.", ct);
    }

    private async Task HandleTextMessage(Message message, string text, CancellationToken ct)
    {
        if (_agentesPorChat.TryGetValue(message.Chat.Id, out var cmd))
        {
            var agente = _agenteLoader.ObterPorComando(cmd);
            if (agente != null)
            {
                await _gateway!.SendChatActionAsync(message.Chat.Id, ct);
                
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
                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "Esse assunto esta fora do meu escopo. Posso ajudar com marketing, combinado?", ct);
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

                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, "🚀 Produzindo...", ct);

                ResultadoPipeline resultadoPipeline;

                if (resultado.Brief.Canal.Equals("email", StringComparison.OrdinalIgnoreCase))
                {
                    resultadoPipeline = await ExecutarPipelineEmail(chatId, resultado, mensagemOriginal, ct);
                }
                else
                {
                    var briefing = RenderizarBrief(resultado);
                    resultadoPipeline = await _loop.ExecutarAsync(
                        chatId,
                        briefing,
                        async (progresso) =>
                        {
                            try
                            {
                                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, progresso, ct);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Erro ao atualizar progresso");
                            }
                        },
                        ct,
                        pedidoOriginal: mensagemOriginal);
                }

                if (IsEntregavelHtml(resultadoPipeline.RespostaFinal))
                {
                    await EnviarHtmlZipAsync(chatId, resultadoPipeline, ct);
                }
                else
                {
                    await EnviarDeckImagensAsync(chatId, resultadoPipeline.Imagens, ct);

                    foreach (var asset in resultadoPipeline.AssetsAnexados)
                    {
                        await EnviarFotoComLegendaAsync(chatId, asset.Bytes, asset.Legenda, ct);
                    }

                    if (!string.IsNullOrEmpty(resultadoPipeline.RespostaFinal))
                    {
                        if (resultadoPipeline.Imagens.Count == 0 && resultadoPipeline.AssetsAnexados.Count == 0)
                        {
                            try
                            {
                                await _gateway!.EditMessageTextAsync(chatId, mensagemProgressoId, resultadoPipeline.RespostaFinal, ct);
                            }
                            catch
                            {
                                await EnviarMensagemLongaAsync(chatId, resultadoPipeline.RespostaFinal, ct);
                            }
                        }
                        else
                        {
                            await EnviarMensagemLongaAsync(chatId, resultadoPipeline.RespostaFinal, ct);
                        }
                    }
                }
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

        return await runner.ExecutarAsync(context, steps, maxRefacoesQa: 2, onProgresso: null, ct);
    }

    private static string RenderizarBrief(RouterResultado resultado)
    {
        var b = resultado.Brief!;
        var sb = new StringBuilder();
        sb.AppendLine($"Canal: {b.Canal}");
        if (!string.IsNullOrEmpty(b.Objetivo)) sb.AppendLine($"Objetivo: {b.Objetivo}");
        if (!string.IsNullOrEmpty(b.Publico)) sb.AppendLine($"Publico: {b.Publico}");
        if (!string.IsNullOrEmpty(b.Oferta)) sb.AppendLine($"Oferta: {b.Oferta}");
        if (!string.IsNullOrEmpty(b.Tom)) sb.AppendLine($"Tom: {b.Tom}");
        if (!string.IsNullOrEmpty(b.Link)) sb.AppendLine($"Link: {b.Link}");
        if (b.Restricoes.Count > 0) sb.AppendLine($"Restricoes: {string.Join(", ", b.Restricoes)}");
        if (b.Imagens.Count > 0)
        {
            sb.AppendLine("Imagens a gerar:");
            foreach (var img in b.Imagens)
                sb.AppendLine($"  - {img.Papel}: {img.Descricao}");
        }
        return sb.ToString();
    }

    private static bool IsEntregavelHtml(string entregavel)
    {
        return entregavel.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || entregavel.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
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

            _historico.AdicionarMensagem(message.Chat.Id, "user", contexto ?? "[imagem]");
            _historico.AdicionarMensagem(message.Chat.Id, "assistant", resposta);

            await EnviarMensagemLongaAsync(message.Chat.Id, resposta, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar foto");
            await _gateway!.SendMessageAsync(message.Chat.Id, "Erro ao analisar a imagem. Tente novamente.", ct);
        }
    }

    private async Task EnviarComStreaming(long chatId, Func<Task<IAsyncEnumerable<string>>> obterStream, CancellationToken ct)
    {
        int? mensagemId = null;

        await _streaming.ProcessarStreamingAsync(
            chatId,
            await obterStream(),
            async (texto) =>
            {
                var msg = await _gateway!.SendMessageAsync(chatId, texto, ct);
                mensagemId = msg.MessageId;
                return mensagemId.Value;
            },
            async (msgId, texto) =>
            {
                await _gateway!.EditMessageTextAsync(chatId, (int)msgId, texto, ct);
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

    private async Task EnviarMensagemLongaAsync(long chatId, string texto, CancellationToken ct)
    {
        var partes = TelegramMessageSplitter.Dividir(texto);
        foreach (var parte in partes)
        {
            await _gateway!.SendMessageAsync(chatId, parte, ct);
        }
    }

    private async Task EnviarFotoComLegendaAsync(long chatId, byte[] bytes, string? legenda, CancellationToken ct)
    {
        var (caption, overflow) = TelegramMessageSplitter.DividirLegenda(legenda);
        using var stream = new MemoryStream(bytes);
        await _gateway!.SendPhotoAsync(chatId, stream, caption, ct);
        foreach (var parte in overflow)
        {
            await _gateway!.SendMessageAsync(chatId, parte, ct);
        }
    }

    private async Task EnviarDeckImagensAsync(long chatId, List<ImagemGerada> imagens, CancellationToken ct)
    {
        if (imagens.Count == 0) return;

        if (imagens.Count == 1)
        {
            await EnviarFotoComLegendaAsync(chatId, imagens[0].Bytes, imagens[0].Legenda, ct);
            return;
        }

        try
        {
            var total = imagens.Count;
            var fotos = imagens.Select((img, i) =>
            {
                var slideLabel = $"Slide {i + 1}/{total}";
                var caption = !string.IsNullOrEmpty(img.Legenda) ? $"{slideLabel} — {img.Legenda}" : slideLabel;
                var (captionTruncada, _) = TelegramMessageSplitter.DividirLegenda(caption);
                return (new MemoryStream(img.Bytes) as Stream, captionTruncada);
            }).ToList();

            await _gateway!.SendMediaGroupAsync(chatId, fotos, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar media group, fallback para envio individual");
            foreach (var img in imagens)
            {
                await EnviarFotoComLegendaAsync(chatId, img.Bytes, img.Legenda, ct);
            }
        }
    }
}
