namespace DemoAgencia.Worker.IA;

public interface IAnalisadorImagem
{
    Task<string> DescreverImagemAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default);
}
