using System.IO.Compression;
using System.Text;
using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Telegram.Bot;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramBotGatewayTests
{
    [Fact]
    public async Task SendDocumentAsync_DeveIncluirFileNameNoMultipart()
    {
        var capturedContent = new TaskCompletionSource<string>();
        var handler = new CapturingHandler(capturedContent);
        var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", httpClient);
        var gateway = new TelegramBotGateway(botClient);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("teste"));

        await gateway.SendDocumentAsync(123, stream, "entregavel.zip", "Entregavel HTML completo com assets", CancellationToken.None);

        var content = await capturedContent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.Should().Contain("filename=\"entregavel.zip\"");
    }

    private class CapturingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<string> _capturedContent;

        public CapturingHandler(TaskCompletionSource<string> capturedContent)
        {
            _capturedContent = capturedContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = string.Empty;
            if (request.Content is MultipartFormDataContent multipart)
            {
                var sb = new StringBuilder();
                foreach (var part in multipart)
                {
                    if (part.Headers.ContentDisposition != null)
                    {
                        sb.AppendLine(part.Headers.ContentDisposition.ToString());
                    }
                }
                body = sb.ToString();
            }

            _capturedContent.TrySetResult(body);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ok\":true,\"result\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":123,\"type\":\"private\"}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
