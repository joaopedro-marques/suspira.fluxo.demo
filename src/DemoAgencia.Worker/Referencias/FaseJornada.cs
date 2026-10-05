namespace DemoAgencia.Worker.Referencias;

public static class FaseJornada
{
    public const string PosCompra = "pos-compra";
    public const string PreChaves = "pre-chaves";
    public const string PosChaves = "pos-chaves";

    public static readonly IReadOnlyList<string> Todas = new[] { PosCompra, PreChaves, PosChaves };

    public static string? Normalizar(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var texto = input.Trim().ToLowerInvariant()
            .Replace("ó", "o").Replace("é", "e").Replace("ê", "e")
            .Replace("á", "a").Replace("ã", "a").Replace("í", "i")
            .Replace("ç", "c").Replace("_", "-").Replace(" ", "-");

        if (texto.Contains("pos-compra") || texto.Contains("pos-compras") || texto.Contains("pós-compra"))
            return PosCompra;

        if (texto.Contains("pre-chaves") || texto.Contains("pré-chaves"))
            return PreChaves;

        if (texto.Contains("pos-chaves") || texto.Contains("pós-chaves"))
            return PosChaves;

        return null;
    }
}
