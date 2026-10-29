namespace DemoAgencia.Worker.Referencias;

/// <summary>
/// Configuracao do header declarada no asset do cliente ({CLIENTE}_header.json).
/// Permite mudar ordem das colunas, clip-path e angulo do gradiente sem tocar em codigo.
/// </summary>
public class HeaderConfig
{
    public const string ClipPathPadraoPadrao =
        "clip-path: polygon(0 0, 100% 0, 100% calc(100% - 8px), calc(100% - 7px) 100%, 0 calc(100% - 34px));";

    public const string ClipPathInvertidoPadrao =
        "clip-path: polygon(0 0, 100% 0, 100% calc(100% - 34px), 7px 100%, 0 calc(100% - 8px));";

    public const string GradienteAnguloPadrao = "135deg";

    /// <summary>Scrim padrao sobre a foto quando o hero vira fundo do header.</summary>
    public const string ScrimPadrao = "rgba(0, 0, 0, 0.45)";

    public static readonly IReadOnlyList<string> ColunasPadrao = new[] { "titulo", "hero" };

    /// <summary>Ordem dos blocos declarados em header_section.html.</summary>
    public List<string> Colunas { get; set; } = new(ColunasPadrao);

    /// <summary>Sorteia a ordem das colunas (hero antes ou depois do titulo) a cada entrega.</summary>
    public bool SortearOrdem { get; set; } = true;

    /// <summary>Sem hero, o header nao e renderizado (mantem o comportamento legado).</summary>
    public bool RequerHero { get; set; } = true;

    public string ClipPathPadrao { get; set; } = ClipPathPadraoPadrao;
    public string ClipPathInvertido { get; set; } = ClipPathInvertidoPadrao;
    public string GradienteAngulo { get; set; } = GradienteAnguloPadrao;

    /// <summary>Overrides de layout/fundo por fase (chave = fase normalizada, ex.: pos-compra).</summary>
    public Dictionary<string, HeaderFaseConfig> PorFase { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Override da fase, ou null quando a fase usa a configuracao global.</summary>
    public HeaderFaseConfig? ObterFase(string? fase)
    {
        return !string.IsNullOrWhiteSpace(fase) && PorFase.TryGetValue(fase.Trim(), out var config)
            ? config
            : null;
    }
}

/// <summary>
/// Override do header para uma fase especifica, declarado em <c>por_fase</c>
/// no <c>{CLIENTE}_header.json</c>.
/// </summary>
public class HeaderFaseConfig
{
    /// <summary><c>gradiente</c> (padrao) ou <c>hero</c> (a foto ocupa o fundo do header).</summary>
    public string? Fundo { get; set; }

    /// <summary>Cor sobre a foto de fundo (rgba recomendado). Ausente = scrim padrao.</summary>
    public string? Scrim { get; set; }

    /// <summary>
    /// Cor solida de fallback para clientes que ignoram background-image (Outlook desktop).
    /// Ausente = primeira cor do gradiente da fase.
    /// </summary>
    public string? CorFundo { get; set; }

    /// <summary>Ordem dos blocos nesta fase. Ausente = configuracao global.</summary>
    public List<string>? Colunas { get; set; }

    /// <summary>Sorteio de ordem nesta fase. Ausente = configuracao global.</summary>
    public bool? SortearOrdem { get; set; }

    public bool UsaHeroComoFundo =>
        string.Equals(Fundo?.Trim(), "hero", StringComparison.OrdinalIgnoreCase);
}
