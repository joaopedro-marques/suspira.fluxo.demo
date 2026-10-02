namespace DemoAgencia.Worker.IA;

public interface IStreamingChat
{
    IAsyncEnumerable<string> CompletarStreamingAsync(
        long chatId,
        string mensagem,
        string? persona,
        string modelo,
        IHistoricoChat historico,
        CancellationToken ct = default);
}
