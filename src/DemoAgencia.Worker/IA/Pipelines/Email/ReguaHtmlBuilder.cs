using System.Globalization;
using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Desenha a regua de jornada em HTML (stepper com marcadores + labels) para o header do email.
/// O markup e os estilos vivem no asset <c>components/regua_section.html</c>; aqui so
/// entram os valores de runtime (cor por estado, largura e label de cada etapa).
/// As cores declaradas no asset sao usadas como estao: o contraste e validado no
/// contrato dos assets, nao corrigido em runtime.
/// </summary>
internal static class ReguaHtmlBuilder
{
    /// <summary>Componente usado quando o cliente nao declara <c>regua_section.html</c>.</summary>
    internal const string ComponentePadrao = """
        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
            <tr>{{regua_etapas}}</tr>
        </table>

        <!--#bloco etapa-concluida-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 0;">
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_esquerda}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="32" height="20" align="center" valign="middle" style="width: 32px; height: 20px; padding: 0 6px; font-size: 0; line-height: 0;">
                        <table role="presentation" width="20" height="20" border="0" cellpadding="0" cellspacing="0" align="center">
                            <tr><td width="20" height="20" align="center" valign="middle" style="width: 20px; height: 20px; background-color: {{cor_marcador}}; border-radius: 20px; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_direita}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                </tr>
            </table>
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td align="center" valign="top" style="padding: 10px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; line-height: 14px; color: {{cor_label}};">{{label}}</td>
                </tr>
            </table>
        </td>
        <!--#/bloco-->

        <!--#bloco etapa-atual-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 0;">
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_esquerda}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="32" height="20" align="center" valign="middle" style="width: 32px; height: 20px; padding: 0 6px; font-size: 0; line-height: 0;">
                        <table role="presentation" width="20" height="20" border="0" cellpadding="0" cellspacing="0" align="center">
                            <tr><td width="20" height="20" align="center" valign="middle" style="width: 20px; height: 20px; background-color: {{cor_marcador}}; border-radius: 20px; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_direita}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                </tr>
            </table>
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td align="center" valign="top" style="padding: 10px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; line-height: 14px; color: {{cor_label}}; font-weight: bold;">{{label}}</td>
                </tr>
            </table>
        </td>
        <!--#/bloco-->

        <!--#bloco etapa-futura-->
        <td width="{{largura}}%" align="center" valign="top" style="padding: 0;">
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_esquerda}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="32" height="20" align="center" valign="middle" style="width: 32px; height: 20px; padding: 0 6px; font-size: 0; line-height: 0;">
                        <table role="presentation" width="16" height="16" border="0" cellpadding="0" cellspacing="0" align="center">
                            <tr><td width="16" height="16" align="center" valign="middle" style="width: 16px; height: 16px; background-color: transparent; border: 2px solid {{cor_marcador}}; border-radius: 16px; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                    <td width="50%" height="20" valign="middle" style="height: 20px;">
                        <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                            <tr><td height="4" style="height: 4px; background-color: {{cor_direita}}; font-size: 0; line-height: 0;">&nbsp;</td></tr>
                        </table>
                    </td>
                </tr>
            </table>
            <table role="presentation" width="100%" border="0" cellpadding="0" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr>
                    <td align="center" valign="top" style="padding: 10px 4px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; line-height: 14px; color: {{cor_label}}; opacity: 0.8;">{{label}}</td>
                </tr>
            </table>
        </td>
        <!--#/bloco-->
        """;

    private static readonly string[] BlocosObrigatorios =
    {
        "etapa-concluida", "etapa-atual", "etapa-futura"
    };

    internal static string Construir(
        EstrategiaEmail? estrategia,
        string? componente = null)
    {
        if (estrategia?.Regua is not { } regua || regua.Etapas.Count == 0 || string.IsNullOrEmpty(estrategia.Fase))
            return string.Empty;

        var etapaAtual = DeterminarEtapaDestacada(estrategia, regua);
        var indiceAtual = regua.Etapas.FindIndex(
            etapa => string.Equals(etapa, etapaAtual, StringComparison.OrdinalIgnoreCase));
        var estilo = ResolverEstilo(estrategia, regua);
        var corConcluida = estilo.CorConcluida;
        var corAtual = estilo.CorAtual;
        var corFutura = estilo.CorFutura;
        var corLabel = estilo.CorLabel;
        var corLabelAtual = estilo.CorLabelAtual;
        var largura = (100.0 / regua.Etapas.Count).ToString("0.##", CultureInfo.InvariantCulture);

        var blocos = ExtrairBlocosDoComponente(componente, out var layout);
        var etapas = new StringBuilder();

        for (var i = 0; i < regua.Etapas.Count; i++)
        {
            var estado = i == indiceAtual ? "atual"
                : i < indiceAtual ? "concluida"
                : "futura";

            // Trecho concluido ate a etapa atual; da atual para frente fica em outline.
            var corEsquerda = i == 0 ? "transparent"
                : i <= indiceAtual ? corConcluida
                : corFutura;
            var corDireita = i == regua.Etapas.Count - 1 ? "transparent"
                : i < indiceAtual ? corConcluida
                : corFutura;

            var corMarcador = estado switch
            {
                "atual" => corAtual,
                "concluida" => corConcluida,
                _ => corFutura
            };

            var corLabelEtapa = estado == "atual" ? corLabelAtual : corLabel;

            etapas.Append(blocos[$"etapa-{estado}"]
                .Replace("{{largura}}", largura)
                .Replace("{{cor_esquerda}}", corEsquerda)
                .Replace("{{cor_direita}}", corDireita)
                .Replace("{{cor_marcador}}", corMarcador)
                .Replace("{{cor_label}}", corLabelEtapa)
                .Replace("{{label}}", WebUtility.HtmlEncode(regua.Etapas[i])));
        }

        return layout.Replace("{{regua_etapas}}", etapas.ToString());
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
    /// a cada fase, entao as cores vem declaradas no asset em <c>estilo_html</c> e, por fase,
    /// em <c>estilo_html_por_fase</c>. O renderer nao altera a cor declarada.
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
