namespace DemoAgencia.Worker.IA.Pipelines;

/// <summary>
/// Normalizacao deterministica do step alvo de refacao/ajuste.
/// </summary>
public static class StepAlvoNormalizer
{
    /// <summary>
    /// Normaliza o step alvo de um ajuste (inclui "template"). Retorna null quando nao reconhece.
    /// </summary>
    public static string? NormalizarAjuste(string? stepAlvo)
    {
        if (string.IsNullOrWhiteSpace(stepAlvo))
            return null;

        var normalizado = stepAlvo.Trim().ToLowerInvariant();

        if (normalizado.Contains("hero") || normalizado.Contains("imagem") || normalizado.Contains("banner"))
            return "hero";
        if (normalizado.Contains("template") || normalizado.Contains("estrutura"))
            return "template";
        if (normalizado.Contains("diagram") || normalizado.Contains("layout") || normalizado.Contains("visual"))
            return "diagramacao";
        if (normalizado.Contains("copy") || normalizado.Contains("texto") || normalizado.Contains("redacao") || normalizado.Contains("redação"))
            return "copy";

        return null;
    }

    /// <summary>
    /// Normalizacao usada pelo QA. O QA nao pode pular para "template" (re-render deterministico
    /// nao corrige conteudo), entao qualquer alvo desconhecido cai em "copy".
    /// </summary>
    public static string NormalizarQa(string? stepAlvo)
    {
        var normalizado = NormalizarAjuste(stepAlvo);
        return normalizado is null or "template" ? "copy" : normalizado;
    }
}
