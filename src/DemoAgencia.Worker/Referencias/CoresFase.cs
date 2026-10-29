namespace DemoAgencia.Worker.Referencias;

/// <summary>
/// Interpreta as cores declaradas na paleta do cliente e separa a identidade da fase
/// das linhas de gradiente fixo da marca.
/// </summary>
public static class CoresFase
{
    /// <summary>
    /// Cores de identidade da fase, ignorando as linhas de gradiente fixo da marca.
    /// Apenas hex validos entram: token corrompido no asset nao vira estilo invalido.
    /// </summary>
    public static IReadOnlyList<string> Identidade(FaseEstrategia? fase)
    {
        if (fase?.CoresHex == null || fase.CoresHex.Count == 0)
            return Array.Empty<string>();

        var identidade = fase.CoresHex
            .Where(linha => !linha.Any(token => token.Equals("gradiente", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(linha => linha)
            .Select(Normalizada)
            .Where(cor => cor != null)
            .Select(cor => cor!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (identidade.Count > 0)
            return identidade;

        return fase.CoresHex
            .SelectMany(linha => linha)
            .Select(Normalizada)
            .Where(cor => cor != null)
            .Select(cor => cor!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Cores efetivamente usadas no gradiente do header (no maximo os dois primeiros stops).</summary>
    public static IReadOnlyList<string> Gradiente(FaseEstrategia? fase)
    {
        var cores = Identidade(fase);
        return cores.Count <= 2 ? cores : cores.Take(2).ToList();
    }

    private static string? Normalizada(string cor)
    {
        return CorHex.TryNormalizar(cor, out var normalizada) ? normalizada : null;
    }
}
