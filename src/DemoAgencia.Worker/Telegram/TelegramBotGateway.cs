using System.Diagnostics.CodeAnalysis;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DemoAgencia.Worker.Telegram;

public class TelegramBotGateway : ITelegramGateway
{
    private readonly TelegramBotClient _botClient;

    public TelegramBotGateway(TelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public async Task<User> GetMeAsync(CancellationToken ct = default)
    {
        return await _botClient.GetMe(ct);
    }

    public async Task<Update[]> GetUpdatesAsync(int offset, int timeout, CancellationToken ct = default)
    {
        return await _botClient.GetUpdates(offset: offset, timeout: timeout, cancellationToken: ct);
    }

    public async Task<Message> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        var textoFormatado = TelegramTextFormatter.RemoverFormatacao(text);
        return await _botClient.SendMessage(chatId: chatId, text: textoFormatado, cancellationToken: ct);
    }

    public async Task EditMessageTextAsync(long chatId, int messageId, string text, CancellationToken ct = default)
    {
        var textoFormatado = TelegramTextFormatter.RemoverFormatacao(text);
        await _botClient.EditMessageText(chatId: chatId, messageId: messageId, text: textoFormatado, cancellationToken: ct);
    }

    public async Task SendDocumentAsync(long chatId, Stream document, string fileName, string? caption, CancellationToken ct = default)
    {
        await _botClient.SendDocument(
            chatId: chatId,
            document: new InputFileStream(document, fileName),
            caption: caption,
            thumbnail: null,
            disableContentTypeDetection: true,
            cancellationToken: ct);
    }

    public async Task SendChatActionAsync(long chatId, CancellationToken ct = default)
    {
        await _botClient.SendChatAction(chatId: chatId, action: ChatAction.Typing, cancellationToken: ct);
    }

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        var file = await _botClient.GetFile(fileId, ct);
        using var memoryStream = new MemoryStream();
        await _botClient.GetInfoAndDownloadFile(file.FileId, memoryStream, ct);
        return memoryStream.ToArray();
    }
}

[ExcludeFromCodeCoverage]
public class TelegramGatewayFactory : ITelegramGatewayFactory
{
    public ITelegramGateway Create(string botToken)
    {
        var botClient = new TelegramBotClient(botToken);
        return new TelegramBotGateway(botClient);
    }
}
