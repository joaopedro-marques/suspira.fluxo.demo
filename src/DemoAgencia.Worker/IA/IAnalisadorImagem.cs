namespace DemoAgencia.Worker.IA;

public interface IAnalisadorImagem
{
    Task<string> DescreverImagemAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default);

    Task<BannerDescricao> DescreverBannerAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default);

    Task<IconDescricao> DescreverIconeAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default);
}
