using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

/// <summary>
/// Preenche o componente de header do cliente. O markup e os estilos vivem no asset
/// (components/header_section.html); aqui so entram os valores de runtime e a ordem
/// das colunas, para que adicionar elementos seja edicao de asset e nao de codigo.
/// </summary>
internal static class HeaderEmailBuilder
{
    internal const string BlocoInicio = "<!--#bloco ";
    internal const string BlocoFim = "<!--#/bloco-->";

    internal const string ClipPathPadrao = HeaderConfig.ClipPathPadraoPadrao;

    internal const string ClipPathInvertido = HeaderConfig.ClipPathInvertidoPadrao;

    internal static string Construir(
        string componente,
        PipelineContext context,
        bool invertido,
        string? reguaComponente = null)
    {
        var fase = context.Estrategia?.FaseDados;
        var header = context.Estrategia?.Header;
        var faseConfig = ResolverConfigFase(header, context.Estrategia?.Fase);
        var usaFundoHero = faseConfig?.UsaHeroComoFundo == true && !string.IsNullOrEmpty(context.HeroSrc);
        var coresFundo = CoresGradiente(fase);
        var corTitulo = CorTitulo(fase, coresFundo);
        var alinhamento = invertido ? "right" : "left";

        var blocos = ExtrairBlocos(componente, out var layout);
        var ordem = faseConfig?.Colunas is { Count: > 0 } colunasFase
            ? colunasFase
            : header?.Colunas is { Count: > 0 } colunas
                ? colunas
                : HeaderConfig.ColunasPadrao;

        var html = layout;
        html = html.Replace("{{header_colunas}}", MontarColunas(
            blocos,
            ordem,
            invertido,
            temHero: !usaFundoHero && !string.IsNullOrEmpty(context.HeroSrc)));
        html = html.Replace("{{header_bg}}", usaFundoHero
            ? ConstruirFundoComHero(fase, header?.GradienteAngulo, context.HeroSrc!, faseConfig?.Scrim, faseConfig?.CorFundo)
            : ConstruirFundo(fase, header?.GradienteAngulo));
        html = html.Replace("{{header_clip}}", invertido
            ? header?.ClipPathInvertido ?? ClipPathInvertido
            : header?.ClipPathPadrao ?? ClipPathPadrao);
        html = html.Replace("{{header_titulo}}", WebUtility.HtmlEncode(context.Copy?.Titulo ?? string.Empty));
        html = html.Replace("{{header_titulo_cor}}", corTitulo);
        html = html.Replace("{{header_titulo_align}}", alinhamento);
        html = html.Replace("{{header_titulo_padding}}",
            alinhamento == "right" ? "padding: 0 0 0 22px;" : "padding: 0 22px 0 0;");
        html = html.Replace("{{header_hero_src}}", WebUtility.HtmlEncode(context.HeroSrc ?? string.Empty));
        html = html.Replace("{{regua_html}}", ReguaHtmlBuilder.Construir(context.Estrategia, reguaComponente));        return html;
    }

    /// <summary>
    /// Extrai os blocos declarados no asset com <c>&lt;!--#bloco nome--&gt; ... &lt;!--#/bloco--&gt;</c>.
    /// O layout devolvido nao contem os blocos, apenas o restante do componente.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ExtrairBlocos(string componente, out string layout)
    {
        var blocos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var restante = new StringBuilder();
        var posicao = 0;

        while (posicao < componente.Length)
        {
            var inicio = componente.IndexOf(BlocoInicio, posicao, StringComparison.Ordinal);
            if (inicio < 0)
            {
                restante.Append(componente, posicao, componente.Length - posicao);
                break;
            }

            restante.Append(componente, posicao, inicio - posicao);

            var fimNome = componente.IndexOf("-->", inicio, StringComparison.Ordinal);
            if (fimNome < 0)
            {
                restante.Append(componente, inicio, componente.Length - inicio);
                break;
            }

            var nome = componente[(inicio + BlocoInicio.Length)..fimNome].Trim();
            var inicioConteudo = fimNome + "-->".Length;
            var fimBloco = componente.IndexOf(BlocoFim, inicioConteudo, StringComparison.Ordinal);
            if (fimBloco < 0)
            {
                restante.Append(componente, inicio, componente.Length - inicio);
                break;
            }

            if (nome.Length > 0)
                blocos[nome] = componente[inicioConteudo..fimBloco].Trim();

            posicao = fimBloco + BlocoFim.Length;
        }

        layout = restante.ToString();
        return blocos;
    }

    /// <summary>Colunas na ordem configurada (ou sorteada); bloco ausente e ignorado.</summary>
    internal static string MontarColunas(
        IReadOnlyDictionary<string, string> blocos,
        IReadOnlyList<string> ordem,
        bool invertido,
        bool temHero = true)
    {
        var efetiva = ordem;
        if (invertido && ordem.Count == 2)
            efetiva = new[] { ordem[1], ordem[0] };

        var sb = new StringBuilder();

        foreach (var nome in efetiva)
        {
            if (!temHero && nome.Equals("hero", StringComparison.OrdinalIgnoreCase))
                continue;

            if (blocos.TryGetValue(nome, out var bloco))
                sb.Append(bloco);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Cor do titulo do header: usa exatamente a cor declarada no asset da fase
    /// (<c>cor_texto_header</c>). Sem declaracao, cai no fallback legivel por contraste.
    /// </summary>
    internal static string CorTitulo(FaseEstrategia? fase, IReadOnlyCollection<string>? coresFundo = null)
    {
        if (CorHex.TryNormalizar(fase?.CorTextoHeader, out var declarada))
            return declarada;

        return CorHex.EscolherTexto(coresFundo ?? CoresGradiente(fase));
    }

    internal static string ConstruirFundo(FaseEstrategia? fase, string? angulo = null)
    {
        var cores = CoresGradiente(fase);
        if (cores.Count == 0)
            return string.Empty;

        var css = $"background-color: {cores[0]};";
        if (cores.Count > 1)
        {
            var anguloFinal = string.IsNullOrWhiteSpace(angulo) ? HeaderConfig.GradienteAnguloPadrao : angulo;
            css += $" background-image: linear-gradient({anguloFinal}, {cores[0]} 0%, {cores[1]} 100%);";
        }

        return css;
    }

    /// <summary>
    /// Fundo do header com a foto full-bleed (fase com <c>fundo: hero</c>). O gradiente
    /// da fase continua como fallback — Outlook desktop ignora background-image — e o
    /// scrim garante contraste do titulo e da regua sobre qualquer foto.
    /// </summary>
    internal static string ConstruirFundoComHero(
        FaseEstrategia? fase,
        string? angulo,
        string heroSrc,
        string? scrim = null,
        string? corFundo = null)
    {
        var cores = CoresGradiente(fase);
        var camada = string.IsNullOrWhiteSpace(scrim) ? HeaderConfig.ScrimPadrao : scrim.Trim();
        var url = $"url('{heroSrc.Replace("'", "%27").Replace("\"", "%22")}')";

        var corBase = CorHex.TryNormalizar(corFundo, out var fallback)
            ? fallback
            : cores.Count > 0 ? cores[0] : null;

        var css = corBase != null ? $"background-color: {corBase}; " : string.Empty;
        return css
            + $"background-image: linear-gradient({camada}, {camada}), {url};"
            + " background-size: cover; background-position: center;";
    }

    /// <summary>Override do header para a fase atual (chave normalizada, ex.: pos-compra).</summary>
    internal static HeaderFaseConfig? ResolverConfigFase(HeaderConfig? header, string? fase)
        => header?.ObterFase(fase);

    /// <summary>Cores efetivamente usadas no gradiente (no maximo os dois primeiros stops).</summary>
    internal static IReadOnlyList<string> CoresGradiente(FaseEstrategia? fase) => CoresFase.Gradiente(fase);

    /// <summary>
    /// Extrai as cores de identidade da fase (linhas que nao sao os gradientes fixos da marca).
    /// Mantido como API do builder; a regra vive em <see cref="CoresFase"/>.
    /// </summary>
    internal static IReadOnlyList<string> ExtrairCoresFase(FaseEstrategia? fase) => CoresFase.Identidade(fase);
}
