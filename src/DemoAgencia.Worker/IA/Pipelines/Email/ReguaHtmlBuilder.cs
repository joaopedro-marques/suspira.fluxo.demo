using System.Globalization;
using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>Desenha a regua de jornada em HTML (segmentos + labels) para o header do email.</summary>
internal static class ReguaHtmlBuilder
{
    internal static string Construir(EstrategiaEmail? estrategia, IReadOnlyCollection<string>? fundosHeader = null)
    {
        if (estrategia?.Regua is not { } regua || regua.Etapas.Count == 0 || string.IsNullOrEmpty(estrategia.Fase))
            return string.Empty;

        var etapaAtual = DeterminarEtapaDestacada(estrategia, regua);
        var indiceAtual = regua.Etapas.FindIndex(
            etapa => string.Equals(etapa, etapaAtual, StringComparison.OrdinalIgnoreCase));
        var estilo = ResolverEstilo(estrategia, regua);
        var (corConcluida, corAtual, corFutura, corLabel, corLabelAtual) = AjustarCores(estilo, fundosHeader);
        var largura = (100.0 / regua.Etapas.Count).ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse: collapse; width: 100%;\">");
        sb.Append("<tr>");

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var cor = i == indiceAtual ? corAtual
                : i < indiceAtual ? corConcluida
                : corFutura;

            sb.Append($"<td width=\"{largura}%\" height=\"6\" style=\"height: 6px; background-color: {cor}; font-size: 0; line-height: 0;\">&nbsp;</td>");
        }

        sb.Append("</tr><tr>");

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var atual = i == indiceAtual;
            var futura = i > indiceAtual;
            var corLabelEtapa = atual ? corLabelAtual : corLabel;
            var peso = atual ? "bold" : "normal";
            var opacidade = futura ? " opacity: 0.8;" : "";
            var label = WebUtility.HtmlEncode(regua.Etapas[i]);

            sb.Append($"<td width=\"{largura}%\" align=\"center\" valign=\"top\" style=\"padding: 8px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 10px; line-height: 12px; color: {corLabelEtapa}; font-weight: {peso};{opacidade}\">{label}</td>");
        }

        sb.Append("</tr></table>");
        return sb.ToString();
    }

    /// <summary>
    /// Estilo da regua para a fase atual. A regua fica sobre o fundo do header, que muda
    /// a cada fase, entao o asset pode declarar overrides em <c>estilo_html_por_fase</c>.
    /// </summary>
    internal static EstiloReguaHtml ResolverEstilo(EstrategiaEmail estrategia, ReguaJornada regua)
    {
        if (!string.IsNullOrEmpty(estrategia.Fase)
            && regua.EstiloHtmlPorFase.TryGetValue(estrategia.Fase, out var porFase))
        {
            return porFase;
        }

        return regua.EstiloHtml;
    }

    /// <summary>
    /// Garante que as cores da regua nao sumam no fundo do header: substitui apenas o que
    /// ficar abaixo do piso de legibilidade (as cores declaradas que passam sao mantidas).
    /// </summary>
    internal static (string concluida, string atual, string futura, string label, string labelAtual) AjustarCores(
        EstiloReguaHtml estilo,
        IReadOnlyCollection<string>? fundosHeader)
    {
        if (fundosHeader == null || fundosHeader.Count == 0)
            return (estilo.CorConcluida, estilo.CorAtual, estilo.CorFutura, estilo.CorLabel, estilo.CorLabelAtual);

        return (
            CorHex.EscolherVisivel(fundosHeader, estilo.CorConcluida),
            CorHex.EscolherVisivel(fundosHeader, estilo.CorAtual),
            estilo.CorFutura,
            CorHex.EscolherTexto(fundosHeader, estilo.CorLabel),
            CorHex.EscolherTexto(fundosHeader, estilo.CorLabelAtual));
    }

    internal static string DeterminarEtapaDestacada(EstrategiaEmail estrategia, ReguaJornada regua)
    {
        var marco = estrategia.SubJornada;
        if (!string.IsNullOrEmpty(marco))
        {
            var marcoNorm = NormalizarTexto(marco);

            foreach (var etapa in regua.Etapas)
            {
                var etapaNorm = NormalizarTexto(etapa);
                foreach (var palavra in etapaNorm.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (palavra.Length > 2 && marcoNorm.Contains(palavra))
                        return etapa;
                }
            }
        }

        if (!string.IsNullOrEmpty(estrategia.Fase) &&
            regua.EtapaDestacadaPorFase.TryGetValue(estrategia.Fase, out var etapaDestacada))
        {
            return etapaDestacada;
        }

        return regua.Etapas.Count > 0 ? regua.Etapas[0] : string.Empty;
    }

    private static string NormalizarTexto(string texto)
    {
        return texto.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i").Replace("ó", "o").Replace("ô", "o")
            .Replace("ú", "u").Replace("ã", "a").Replace("õ", "o")
            .Replace("ç", "c");
    }
}
