namespace DemoAgencia.Worker.IA;

public interface IGeradorImagem
{
    Task<byte[]?> GerarImagemAsync(
        long chatId,
        string prompt,
        CancellationToken ct = default);
}
