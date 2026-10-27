using System.Globalization;
using System.Text.RegularExpressions;

namespace DemoAgencia.Worker.Referencias;

/// <summary>
/// Utilitarios de cor para o HTML do email: normaliza hex, calcula contraste (WCAG)
/// e escolhe cores legiveis para texto, moldura e regua sobre o fundo do header.
/// </summary>
public static partial class CorHex
{
    /// <summary>WCAG AA para texto grande (&gt;= 18.66px bold ou &gt;= 24px).</summary>
    public const double ContrasteMinimoTexto = 3.0;

    /// <summary>
    /// Piso absoluto de legibilidade. Uma cor declarada no asset e respeitada enquanto
    /// ficar acima disto; abaixo, o sistema substitui automaticamente.
    /// </summary>
    public const double ContrasteMinimoAbsoluto = 2.5;

    /// <summary>Contraste minimo para elementos decorativos (segmentos da regua e molduras).</summary>
    public const double ContrasteMinimoDecorativo = 1.5;

    public const string TextoClaroPadrao = "#FFFFFF";
    public const string TextoEscuroPadrao = "#1A1A1A";
    public const string MolduraPadrao = "#FFFFFF";

    /// <summary>Normaliza um hex (#RGB ou #RRGGBB) e rejeita qualquer outro formato.</summary>
    public static bool TryNormalizar(string? valor, out string cor)
    {
        cor = string.Empty;
        var limpo = valor?.Trim();
        if (string.IsNullOrEmpty(limpo) || !HexRegex().IsMatch(limpo))
            return false;

        cor = limpo.Length == 4
            ? $"#{limpo[1]}{limpo[1]}{limpo[2]}{limpo[2]}{limpo[3]}{limpo[3]}".ToUpperInvariant()
            : limpo.ToUpperInvariant();
        return true;
    }

    public static bool EhHexValido(string? valor) => TryNormalizar(valor, out _);

    /// <summary>Luminancia relativa (WCAG). Cor invalida resulta em 0.</summary>
    public static double Luminancia(string cor)
    {
        if (!TryNormalizar(cor, out var normalizada))
            return 0;

        var r = Canal(Componente(normalizada, 1));
        var g = Canal(Componente(normalizada, 3));
        var b = Canal(Componente(normalizada, 5));
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>Razao de contraste entre duas cores (1 = iguais, 21 = preto vs branco).</summary>
    public static double Contraste(string a, string b)
    {
        if (!EhHexValido(a) || !EhHexValido(b))
            return 0;

        var la = Luminancia(a);
        var lb = Luminancia(b);
        var maior = Math.Max(la, lb);
        var menor = Math.Min(la, lb);
        return (maior + 0.05) / (menor + 0.05);
    }

    /// <summary>Pior contraste da cor contra todos os stops do fundo (o caso critico).</summary>
    public static double ContrasteMinimo(IEnumerable<string> fundos, string cor)
    {
        var validos = fundos.Where(EhHexValido).ToList();
        return validos.Count == 0 ? 0 : validos.Min(fundo => Contraste(fundo, cor));
    }

    /// <summary>
    /// Cor de texto legivel sobre o fundo informado. Respeita a cor declarada no asset
    /// enquanto ela passar do piso absoluto; caso contrario escolhe entre claro e escuro.
    /// </summary>
    public static string EscolherTexto(IReadOnlyCollection<string> fundos, string? preferida = null)
    {
        if (fundos.Count == 0)
            return TextoClaroPadrao;

        if (TryNormalizar(preferida, out var preferidaNormalizada)
            && ContrasteMinimo(fundos, preferidaNormalizada) >= ContrasteMinimoAbsoluto)
        {
            return preferidaNormalizada;
        }

        return ContrasteMinimo(fundos, TextoClaroPadrao) >= ContrasteMinimo(fundos, TextoEscuroPadrao)
            ? TextoClaroPadrao
            : TextoEscuroPadrao;
    }

    /// <summary>
    /// Cor visivel (moldura, segmento) sobre o fundo. Se a cor declarada sumir no fundo,
    /// troca pela cor de texto legivel.
    /// </summary>
    public static string EscolherVisivel(
        IReadOnlyCollection<string> fundos,
        string? preferida,
        double minimo = ContrasteMinimoDecorativo)
    {
        if (TryNormalizar(preferida, out var preferidaNormalizada)
            && (fundos.Count == 0 || ContrasteMinimo(fundos, preferidaNormalizada) >= minimo))
        {
            return preferidaNormalizada;
        }

        return EscolherTexto(fundos);
    }

    private static double Componente(string hex, int inicio)
    {
        return int.Parse(hex.Substring(inicio, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    }

    private static double Canal(double componente)
    {
        return componente <= 0.03928
            ? componente / 12.92
            : Math.Pow((componente + 0.055) / 1.055, 2.4);
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexRegex();
}
