namespace DemoAgencia.Worker.IA;

public interface IGeradorImagem
{
    Task<ResultadoImagem> GerarImagemAsync(
        long chatId,
        string prompt,
        CancellationToken ct = default);
}
