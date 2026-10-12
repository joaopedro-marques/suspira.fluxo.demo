using System.Text;
using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramBotGatewayInlineKeyboardTests
{
    [Fact]
    public async Task SendMessageAsync_ComKeyboard_DeveIncluirReplyMarkupNoRequest()
    {
        var capturedBody = new TaskCompletionSource<string>();
        var handler = new CapturingHandler(capturedBody);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Opção 1", "callback1"),
                InlineKeyboardButton.WithCallbackData("Opção 2", "callback2")
            }
        });

        await gateway.SendMessageAsync(123, "teste", keyboard, CancellationToken.None);

        var body = await capturedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        body.Should().Contain("reply_markup");
        body.Should().Contain("callback1");
        body.Should().Contain("callback2");
    }

    [Fact]
    public async Task SendMessageAsync_SemKeyboard_NaoDeveIncluirReplyMarkup()
    {
        var capturedBody = new TaskCompletionSource<string>();
        var handler = new CapturingHandler(capturedBody);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        await gateway.SendMessageAsync(123, "teste", keyboard: null, CancellationToken.None);

        var body = await capturedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        body.Should().NotContain("reply_markup");
    }

    [Fact]
    public async Task EditMessageTextAsync_ComKeyboard_DeveIncluirReplyMarkupNoRequest()
    {
        var capturedBody = new TaskCompletionSource<string>();
        var handler = new CapturingHandler(capturedBody);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Confirmar", "onb|confirmar")
            }
        });

        await gateway.EditMessageTextAsync(123, 42, "editado", keyboard, CancellationToken.None);

        var body = await capturedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        body.Should().Contain("reply_markup");
        body.Should().Contain("onb|confirmar");
        body.Should().Contain("editado");
    }

    [Fact]
    public async Task AnswerCallbackQueryAsync_DeveIncluirCallbackQueryId()
    {
        var capturedBody = new TaskCompletionSource<string>();
        var handler = new AnswerCallbackCapturingHandler(capturedBody);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        await gateway.AnswerCallbackQueryAsync("query123", CancellationToken.None);

        var body = await capturedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        body.Should().Contain("query123");
    }

    [Fact]
    public async Task EditMessageReplyMarkupAsync_DeveEnviarRequest()
    {
        var capturedBody = new TaskCompletionSource<string>();
        var handler = new CapturingHandler(capturedBody);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        await gateway.EditMessageReplyMarkupAsync(123, 42, CancellationToken.None);

        var body = await capturedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        body.Should().Contain("123");
        body.Should().Contain("42");
    }

    [Fact]
    public async Task EditMessageReplyMarkupAsync_QuandoMessageNotModified_NaoDeveLancar()
    {
        var handler = new NotModifiedHandler();
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        var act = async () => await gateway.EditMessageReplyMarkupAsync(123, 42, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private class NotModifiedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private class CapturingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<string> _capturedBody;

        public CapturingHandler(TaskCompletionSource<string> capturedBody)
        {
            _capturedBody = capturedBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _capturedBody.TrySetResult(body);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ok\":true,\"result\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":123,\"type\":\"private\"}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private class AnswerCallbackCapturingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<string> _capturedBody;

        public AnswerCallbackCapturingHandler(TaskCompletionSource<string> capturedBody)
        {
            _capturedBody = capturedBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _capturedBody.TrySetResult(body);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ok\":true,\"result\":true}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
