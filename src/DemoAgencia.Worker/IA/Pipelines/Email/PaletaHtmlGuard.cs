using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Guarda determinística da paleta da fase: cores cromáticas fora das cores de
/// identidade declaradas no asset são trocadas pela cor permitida mais próxima.
/// Cinzas (inclusive branco e preto) continuam permitidos para texto, bordas e
/// divisores, porque são estruturais e não carregam a identidade da etapa.
/// </summary>
internal static partial class PaletaHtmlGuard
{
    internal static IReadOnlyCollection<string> CoresPermitidas(FaseEstrategia? fase)
    {
        var cores = new HashSet<string>(CoresFase.Identidade(fase), StringComparer.OrdinalIgnoreCase);

        if (CorHex.TryNormalizar(fase?.CorTextoHeader, out var texto))
            cores.Add(texto);

        return cores;
    }

    internal static string Aplicar(
        string html,
        FaseEstrategia? fase,
        out IReadOnlyDictionary<string, string> substituicoes)
    {
        var trocas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        substituicoes = trocas;

        if (string.IsNullOrEmpty(html))
            return html;

        var permitidas = CoresPermitidas(fase);
        if (permitidas.Count == 0)
            return html;

        return AtributoComCorRegex().Replace(html, match =>
        {
            var valor = match.Groups["valor"].Value;
            var ajustado = HexRegex().Replace(valor, hex =>
            {
                var token = hex.Value;
                if (!CorHex.TryNormalizar(token, out var cor))
                    return token;

                if (EhNeutra(cor) || permitidas.Contains(cor))
                    return token;

                var escolhida = MaisProxima(permitidas, cor);
                trocas[cor] = escolhida;
                return escolhida;
            });

            if (ajustado == valor)
                return match.Value;

            return $"{match.Groups["prefixo"].Value}{match.Groups["quote"].Value}{ajustado}{match.Groups["quote"].Value}";
        });
    }

    /// <summary>#RRGGBB em que os três canais são iguais: branco, preto ou cinza.</summary>
    private static bool EhNeutra(string cor)
        => cor.Length == 7
           && cor[1..3] == cor[3..5]
           && cor[3..5] == cor[5..7];

    private static string MaisProxima(IReadOnlyCollection<string> permitidas, string cor)
    {
        var alvo = Rgb(cor);

        return permitidas
            .OrderBy(c => Distancia(alvo, Rgb(c)))
            .ThenBy(c => c, StringComparer.Ordinal)
            .First();
    }

    private static (int R, int G, int B) Rgb(string cor)
        => (Convert.ToInt32(cor.Substring(1, 2), 16),
            Convert.ToInt32(cor.Substring(3, 2), 16),
            Convert.ToInt32(cor.Substring(5, 2), 16));

    /// <summary>Distância ponderada (redmean), mais próxima da percepção que RGB puro.</summary>
    private static double Distancia((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        var mediaR = (a.R + b.R) / 2.0;

        return (2 + mediaR / 256) * dr * dr
               + 4 * dg * dg
               + (2 + (255 - mediaR) / 256) * db * db;
    }

    [GeneratedRegex(@"(?<prefixo>\b(?:style|bgcolor|color)\s*=\s*)(?<quote>[""'])(?<valor>.*?)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex AtributoComCorRegex();

    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b",
        RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex HexRegex();
}
