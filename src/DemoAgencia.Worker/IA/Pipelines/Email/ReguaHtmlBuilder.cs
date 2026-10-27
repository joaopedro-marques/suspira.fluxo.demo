using System.Globalization;
using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>Desenha a regua de jornada em HTML (segmentos + labels) para o header do email.</summary>
internal static class ReguaHtmlBuilder
{
    internal static string Construir(EstrategiaEmail? estrategia)
    {
        if (estrategia?.Regua is not { } regua || regua.Etapas.Count == 0 || string.IsNullOrEmpty(estrategia.Fase))
            return string.Empty;

        var etapaAtual = DeterminarEtapaDestacada(estrategia, regua);
        var indiceAtual = regua.Etapas.FindIndex(
            etapa => string.Equals(etapa, etapaAtual, StringComparison.OrdinalIgnoreCase));
        var estilo = regua.EstiloHtml;
        var largura = (100.0 / regua.Etapas.Count).ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse: collapse; width: 100%;\">");
        sb.Append("<tr>");

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var cor = i == indiceAtual ? estilo.CorAtual
                : i < indiceAtual ? estilo.CorConcluida
                : estilo.CorFutura;

            sb.Append($"<td width=\"{largura}%\" height=\"6\" style=\"height: 6px; background-color: {cor}; font-size: 0; line-height: 0;\">&nbsp;</td>");
        }

        sb.Append("</tr><tr>");

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var atual = i == indiceAtual;
            var futura = i > indiceAtual;
            var corLabel = atual ? estilo.CorLabelAtual : estilo.CorLabel;
            var peso = atual ? "bold" : "normal";
            var opacidade = futura ? " opacity: 0.8;" : "";
            var label = WebUtility.HtmlEncode(regua.Etapas[i]);

            sb.Append($"<td width=\"{largura}%\" align=\"center\" valign=\"top\" style=\"padding: 8px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 10px; line-height: 12px; color: {corLabel}; font-weight: {peso};{opacidade}\">{label}</td>");
        }

        sb.Append("</tr></table>");
        return sb.ToString();
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
