using System.Net;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Monta o header MRV em HTML: fundo gradiente na cor da etapa, regua de jornada no topo,
/// titulo branco ao lado do hero e margem inferior triangular arredondada (assimetrica).
/// </summary>
internal static class HeaderEmailBuilder
{
    internal const string ClipPathPadrao =
        "clip-path: polygon(0 0, 100% 0, 100% calc(100% - 8px), calc(100% - 7px) 100%, 0 calc(100% - 34px));";

    internal const string ClipPathInvertido =
        "clip-path: polygon(0 0, 100% 0, 100% calc(100% - 34px), 7px 100%, 0 calc(100% - 8px));";

    internal static string Construir(string componente, PipelineContext context, bool invertido)
    {
        var html = componente;
        html = html.Replace("{{header_bg}}", ConstruirFundo(context.Estrategia?.FaseDados));
        html = html.Replace("{{header_clip}}", invertido ? ClipPathInvertido : ClipPathPadrao);
        html = html.Replace("{{regua_html}}", ReguaHtmlBuilder.Construir(context.Estrategia));
        html = html.Replace("{{header_coluna_1}}", invertido
            ? ConstruirCelulaImagem(context.HeroSrc)
            : ConstruirCelulaTitulo(context.Copy?.Titulo, "left"));
        html = html.Replace("{{header_coluna_2}}", invertido
            ? ConstruirCelulaTitulo(context.Copy?.Titulo, "right")
            : ConstruirCelulaImagem(context.HeroSrc));
        return html;
    }

    internal static string ConstruirFundo(FaseEstrategia? fase)
    {
        var cores = ExtrairCoresFase(fase);
        if (cores.Count == 0)
            return string.Empty;

        var css = $"background-color: {cores[0]};";
        if (cores.Count > 1)
            css += $" background-image: linear-gradient(135deg, {cores[0]} 0%, {cores[1]} 100%);";
        return css;
    }

    /// <summary>
    /// Extrai as cores de identidade da fase (linhas que nao sao os gradientes fixos da marca).
    /// Se a fase nao tiver cores proprias, usa qualquer hex da matriz como fallback.
    /// </summary>
    internal static IReadOnlyList<string> ExtrairCoresFase(FaseEstrategia? fase)
    {
        if (fase?.CoresHex == null || fase.CoresHex.Count == 0)
            return Array.Empty<string>();

        var identidade = fase.CoresHex
            .Where(linha => !linha.Any(token => token.Equals("gradiente", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(linha => linha)
            .Where(cor => cor.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (identidade.Count > 0)
            return identidade;

        return fase.CoresHex
            .SelectMany(linha => linha)
            .Where(cor => cor.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string ConstruirCelulaTitulo(string? titulo, string alinhamento)
    {
        var padding = alinhamento == "right" ? "padding: 0 0 0 22px;" : "padding: 0 22px 0 0;";
        var texto = WebUtility.HtmlEncode(titulo ?? string.Empty);

        return "<td width=\"57%\" valign=\"middle\" align=\"" + alinhamento + "\" style=\"" + padding + "\">"
            + "<p style=\"margin: 0; font-family: 'Segoe UI', Arial, sans-serif; font-size: 26px; line-height: 30px; font-weight: bold; color: #FFFFFF;\">"
            + texto
            + "</p></td>";
    }

    internal static string ConstruirCelulaImagem(string? heroSrc)
    {
        var src = WebUtility.HtmlEncode(heroSrc ?? string.Empty);

        return "<td width=\"43%\" valign=\"middle\" align=\"center\" style=\"padding: 0;\">"
            + "<img src=\"" + src + "\" alt=\"Hero\" width=\"240\" border=\"0\" style=\"display: block; width: 100%; max-width: 240px; height: auto; border-radius: 16px;\">"
            + "</td>";
    }
}
