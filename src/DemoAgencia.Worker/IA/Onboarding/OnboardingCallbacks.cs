namespace DemoAgencia.Worker.IA.Onboarding;

public static class OnboardingCallbacks
{
    public const string Prefixo = "onb";
    public const string Pular = "onb|pular";
    public const string SemLink = "onb|sem-link";
    public const string Confirmar = "onb|confirmar";
    public const string Recomecar = "onb|recomecar";

    private static readonly HashSet<string> AcoesEspeciais = new(StringComparer.OrdinalIgnoreCase)
    {
        "pular", "sem-link", "confirmar", "recomecar"
    };

    public static string Criar(OnboardingPasso passo, string valor)
    {
        var passoNome = passo.ToString().ToLowerInvariant();
        return $"{Prefixo}|{passoNome}|{valor}";
    }

    public static bool TentarParse(string callbackData, out OnboardingPasso passo, out string valor)
    {
        passo = default;
        valor = string.Empty;

        if (string.IsNullOrWhiteSpace(callbackData))
            return false;

        var partes = callbackData.Split('|');
        if (partes.Length != 3)
            return false;

        if (!string.Equals(partes[0], Prefixo, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Enum.TryParse<OnboardingPasso>(partes[1], ignoreCase: true, out var passoParsed))
            return false;

        passo = passoParsed;
        valor = partes[2];
        return true;
    }

    public static bool EhAcaoEspecial(string callbackData, out string acao)
    {
        acao = string.Empty;

        if (string.IsNullOrWhiteSpace(callbackData))
            return false;

        var partes = callbackData.Split('|');
        if (partes.Length != 2)
            return false;

        if (!string.Equals(partes[0], Prefixo, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!AcoesEspeciais.Contains(partes[1]))
            return false;

        acao = partes[1].ToLowerInvariant();
        return true;
    }

    public static string ObterAcao(string callbackData)
    {
        var partes = callbackData.Split('|');
        return partes.Length == 2 ? partes[1].ToLowerInvariant() : string.Empty;
    }
}
