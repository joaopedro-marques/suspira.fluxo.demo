using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Referencias;

public enum TipoAsset
{
    Header,
    Footer,
    Icon,
    Logo,
    Foto,
    Post,
    Outro
}

[ExcludeFromCodeCoverage]
public class AssetVisual
{
    public string Id { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public TipoAsset Tipo { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string Caminho { get; init; } = string.Empty;
    public string? Descricao { get; set; }

    public static TipoAsset ParseTipo(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return TipoAsset.Outro;

        return texto.ToLowerInvariant() switch
        {
            "header" => TipoAsset.Header,
            "footer" => TipoAsset.Footer,
            "icon" => TipoAsset.Icon,
            "logo" => TipoAsset.Logo,
            "foto" => TipoAsset.Foto,
            "post" => TipoAsset.Post,
            _ => TipoAsset.Outro
        };
    }
}
