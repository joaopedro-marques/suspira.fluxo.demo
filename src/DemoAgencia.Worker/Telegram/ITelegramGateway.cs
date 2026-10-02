using Telegram.Bot.Types;

namespace DemoAgencia.Worker.Telegram;

public interface ITelegramGateway
{
    Task<User> GetMeAsync(CancellationToken ct = default);
    Task<Update[]> GetUpdatesAsync(int offset, int timeout, CancellationToken ct = default);
    Task<Message> SendMessageAsync(long chatId, string text, CancellationToken ct = default);
    Task EditMessageTextAsync(long chatId, int messageId, string text, CancellationToken ct = default);
    Task SendPhotoAsync(long chatId, Stream photo, string? caption, CancellationToken ct = default);
    Task SendChatActionAsync(long chatId, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
}

public interface ITelegramGatewayFactory
{
    ITelegramGateway Create(string botToken);
}
