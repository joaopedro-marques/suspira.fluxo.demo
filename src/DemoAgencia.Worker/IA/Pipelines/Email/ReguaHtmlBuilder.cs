using System.Globalization;
using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Desenha a regua de jornada em HTML (segmentos + labels) para o header do email.
/// O markup e os estilos vivem no asset <c>components/regua_section.html</c>; aqui so
/// entram os valores de runtime (cor por estado, largura e label de cada etapa).
/// </summary>
internal static class ReguaHtmlBuilder
{
    /// <summary>Componente usado quando o cliente nao declara <c>regua_section.html</c>.</summary>
    internal const string ComponentePadrao = """
        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
            <tr>{{regua_segmentos}}</tr>
            <tr>{{regua_labels}}</tr>
        </table>

        <!--#bloco segmento-->
        <td width="{{largura}}%" height="6" style="height: 6px; background-color: {{cor}}; font-size: 0; line-height: 0;">&nbsp;</td>
        <!--#/bloco-->

        <!--#bloco label-concluida-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 8px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 10px; line-height: 12px; color: {{cor}}; font-weight: normal;">{{label}}</td>
        <!--#/bloco-->

        <!--#bloco label-atual-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 8px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 10px; line-height: 12px; color: {{cor}}; font-weight: bold;">{{label}}</td>
        <!--#/bloco-->

        <!--#bloco label-futura-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 8px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 10px; line-height: 12px; color: {{cor}}; font-weight: normal; opacity: 0.8;">{{label}}</td>
        <!--#/bloco-->
        """;

    private static readonly string[] BlocosObrigatorios =
    {
        "segmento", "label-concluida", "label-atual", "label-futura"
    };

    internal static string Construir(
        EstrategiaEmail? estrategia,
        IReadOnlyCollection<string>? fundosHeader = null,
        string? componente = null)
    {
        if (estrategia?.Regua is not { } regua || regua.Etapas.Count == 0 || string.IsNullOrEmpty(estrategia.Fase))
            return string.Empty;

        var etapaAtual = DeterminarEtapaDestacada(estrategia, regua);
        var indiceAtual = regua.Etapas.FindIndex(
            etapa => string.Equals(etapa, etapaAtual, StringComparison.OrdinalIgnoreCase));
        var estilo = ResolverEstilo(estrategia, regua);
        var (corConcluida, corAtual, corFutura, corLabel, corLabelAtual) = AjustarCores(estilo, fundosHeader);
        var largura = (100.0 / regua.Etapas.Count).ToString("0.##", CultureInfo.InvariantCulture);

        var blocos = ExtrairBlocosDoComponente(componente, out var layout);
        var segmentos = new StringBuilder();
        var labels = new StringBuilder();

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var estado = i == indiceAtual ? "atual"
                : i < indiceAtual ? "concluida"
                : "futura";

            var corSegmento = estado switch
            {
                "atual" => corAtual,
                "concluida" => corConcluida,
                _ => corFutura
            };

            var corLabelEtapa = estado == "atual" ? corLabelAtual : corLabel;

            segmentos.Append(blocos["segmento"]
                .Replace("{{largura}}", largura)
                .Replace("{{cor}}", corSegmento));

            labels.Append(blocos[$"label-{estado}"]
                .Replace("{{largura}}", largura)
                .Replace("{{cor}}", corLabelEtapa)
                .Replace("{{label}}", WebUtility.HtmlEncode(regua.Etapas[i])));
        }

        return layout
            .Replace("{{regua_segmentos}}", segmentos.ToString())
            .Replace("{{regua_labels}}", labels.ToString());
    }

    /// <summary>
    /// Extrai os blocos do componente do cliente. Componente ausente ou incompleto
    /// cai no <see cref="ComponentePadrao"/>, mantendo a regua renderizada.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ExtrairBlocosDoComponente(
        string? componente,
        out string layout)
    {
        if (!string.IsNullOrWhiteSpace(componente))
        {
            var blocos = HeaderEmailBuilder.ExtrairBlocos(componente, out layout);
            if (BlocosObrigatorios.All(blocos.ContainsKey))
                return blocos;
        }

        return HeaderEmailBuilder.ExtrairBlocos(ComponentePadrao, out layout);
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
