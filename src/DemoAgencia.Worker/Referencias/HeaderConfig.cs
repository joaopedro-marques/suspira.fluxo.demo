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
}
