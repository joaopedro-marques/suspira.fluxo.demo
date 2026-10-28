using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Guarda determinística da paleta da fase: cores cromáticas fora das cores de
/// identidade declaradas no asset são trocadas pela cor da etapa mais próxima.
/// Tons claros viram tons claros da cor da etapa (nunca branco), para que o corpo
/// não perca a identidade da jornada. Cinzas (inclusive branco e preto) continuam
/// permitidos para texto, bordas e divisores, porque são estruturais.
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

        // A cor de texto do header (normalmente branco) pode ser usada no HTML, mas
        // nunca serve de destino da substituicao: tons pasteis sao mais proximos do
        // branco do que da cor da etapa e o corpo inteiro acabaria cinza.
        var alvos = CoresFase.Identidade(fase)
            .Where(cor => !EhNeutra(cor))
            .ToList();

        if (permitidas.Count == 0 || alvos.Count == 0)
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

                var escolhida = MaisProxima(alvos, cor);
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

    private static string MaisProxima(IReadOnlyCollection<string> coresDaEtapa, string cor)
    {
        var alvo = Rgb(cor);
        var paleta = coresDaEtapa
            .OrderBy(c => Distancia(alvo, Rgb(c)))
            .ThenBy(c => c, StringComparer.Ordinal)
            .First();

        // Fundos pasteis sao comuns em blocos de destaque. Trocar por um tom saturado
        // mudaria o peso visual da secao (e o contraste do texto); nesse caso devolvemos
        // um tom claro da cor da etapa, preservando o brilho original.
        return EhTomClaro(cor) ? ClarearAte(paleta, CorHex.Luminancia(cor)) : paleta;
    }

    private static bool EhTomClaro(string cor) => CorHex.Luminancia(cor) >= 0.5;

    private static string ClarearAte(string paleta, double luminanciaAlvo)
    {
        var melhor = paleta;
        var melhorDelta = Math.Abs(CorHex.Luminancia(paleta) - luminanciaAlvo);

        for (var passo = 1; passo <= 50; passo++)
        {
            var misturada = MisturarComBranco(paleta, passo / 50.0);
            var delta = Math.Abs(CorHex.Luminancia(misturada) - luminanciaAlvo);

            if (delta < melhorDelta)
            {
                melhor = misturada;
                melhorDelta = delta;
            }

            if (CorHex.Luminancia(misturada) >= luminanciaAlvo)
                break;
        }

        return melhor;
    }

    private static string MisturarComBranco(string cor, double proporcao)
    {
        var (r, g, b) = Rgb(cor);

        static int Canal(int origem, double proporcao)
            => (int)Math.Round(origem + (255 - origem) * proporcao);

        return $"#{Canal(r, proporcao):X2}{Canal(g, proporcao):X2}{Canal(b, proporcao):X2}";
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
