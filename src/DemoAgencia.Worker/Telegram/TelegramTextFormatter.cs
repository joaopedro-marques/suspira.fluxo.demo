using System.Text.RegularExpressions;

namespace DemoAgencia.Worker.Telegram;

public static partial class TelegramTextFormatter
{
    public static string RemoverFormatacao(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;

        var resultado = texto;

        resultado = RemoverNegritoDuploAsteriscoRegex().Replace(resultado, "$1");
        resultado = RemoverNegritoAsteriscoSimplesRegex().Replace(resultado, "$1");
        resultado = RemoverItalicoSublinhadoRegex().Replace(resultado, "$1");
        resultado = RemoverItalicoAsteriscoRegex().Replace(resultado, "$1");
        resultado = RemoverTachadoRegex().Replace(resultado, "$1");
        resultado = RemoverMonoespacoRegex().Replace(resultado, "$1");

        return resultado;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Compiled)]
    private static partial Regex RemoverNegritoDuploAsteriscoRegex();

    [GeneratedRegex(@"\*(.+?)\*", RegexOptions.Compiled)]
    private static partial Regex RemoverNegritoAsteriscoSimplesRegex();

    [GeneratedRegex(@"_(.+?)_", RegexOptions.Compiled)]
    private static partial Regex RemoverItalicoSublinhadoRegex();

    [GeneratedRegex(@"(?<!\w)\*(.+?)\*(?!\w)", RegexOptions.Compiled)]
    private static partial Regex RemoverItalicoAsteriscoRegex();

    [GeneratedRegex(@"~(.+?)~", RegexOptions.Compiled)]
    private static partial Regex RemoverTachadoRegex();

    [GeneratedRegex(@"`(.+?)`", RegexOptions.Compiled)]
    private static partial Regex RemoverMonoespacoRegex();
}
