namespace DemoAgencia.Worker.Referencias;

public static class FaseJornada
{
    // Fases padrão como fallback para quando não há configuração do cliente
    private static readonly Dictionary<string, string[]> FasesPadrao = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pos-compra"] = new[] { "pos-compra", "pós-compra", "pos-compras", "pós-compras" },
        ["pre-chaves"] = new[] { "pre-chaves", "pré-chaves" },
        ["pos-chaves"] = new[] { "pos-chaves", "pós-chaves", "morador" }
    };

    public static string NormalizarTexto(string texto)
    {
        return texto.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i").Replace("ó", "o").Replace("ô", "o")
            .Replace("ú", "u").Replace("ã", "a").Replace("õ", "o")
            .Replace("ç", "c").Replace(" ", "-").Replace("_", "-");
    }

    public static string? Normalizar(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var texto = NormalizarTexto(input);

        // Tenta encontrar nas fases padrão
        foreach (var kvp in FasesPadrao)
        {
            foreach (var variacao in kvp.Value)
            {
                if (texto.Contains(NormalizarTexto(variacao)))
                    return kvp.Key;
            }
        }

        return null;
    }
}
