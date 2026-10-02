namespace DemoAgencia.Worker.IA;

public interface IStreamingService
{
    Task ProcessarStreamingAsync(
        long chatId,
        IAsyncEnumerable<string> chunks,
        Func<string, Task<long>> enviarMensagem,
        Func<long, string, Task> editarMensagem,
        CancellationToken ct);
}

public class StreamingService : IStreamingService
{
    private readonly ILogger<StreamingService> _logger;

    public StreamingService(ILogger<StreamingService> logger)
    {
        _logger = logger;
    }

    public async Task ProcessarStreamingAsync(
        long chatId,
        IAsyncEnumerable<string> chunks,
        Func<string, Task<long>> enviarMensagem,
        Func<long, string, Task> editarMensagem,
        CancellationToken ct)
    {
        var mensagemCompleta = "";
        long? messageId = null;
        var ultimaEdicao = DateTime.MinValue;
        var intervaloEdicao = TimeSpan.FromMilliseconds(1000);

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            mensagemCompleta += chunk;

            if (messageId == null)
            {
                messageId = await enviarMensagem(mensagemCompleta);
                ultimaEdicao = DateTime.Now;
            }
            else if (DateTime.Now - ultimaEdicao >= intervaloEdicao)
            {
                try
                {
                    await editarMensagem(messageId.Value, mensagemCompleta);
                    ultimaEdicao = DateTime.Now;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro ao editar mensagem (rate limit?)");
                }
            }
        }

        if (messageId != null && mensagemCompleta.Length > 0)
        {
            try
            {
                await editarMensagem(messageId.Value, mensagemCompleta);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro na edicao final");
            }
        }

        _logger.LogInformation("Streaming completo: {Length} caracteres", mensagemCompleta.Length);
    }
}
