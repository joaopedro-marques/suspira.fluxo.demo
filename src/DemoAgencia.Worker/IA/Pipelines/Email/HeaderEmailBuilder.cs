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
        var coresFundo = CoresGradiente(fase);
        var corTitulo = CorTitulo(fase, coresFundo);
        var corMoldura = CorMoldura(fase, coresFundo);
        var alinhamento = invertido ? "right" : "left";

        var blocos = ExtrairBlocos(componente, out var layout);
        var ordem = header?.Colunas is { Count: > 0 } colunas ? colunas : HeaderConfig.ColunasPadrao;

        var html = layout;
        html = html.Replace("{{header_colunas}}",
            MontarColunas(blocos, ordem, invertido, temHero: !string.IsNullOrEmpty(context.HeroSrc)));
        html = html.Replace("{{header_bg}}", ConstruirFundo(fase, header?.GradienteAngulo));
        html = html.Replace("{{header_clip}}", invertido
            ? header?.ClipPathInvertido ?? ClipPathInvertido
            : header?.ClipPathPadrao ?? ClipPathPadrao);
        html = html.Replace("{{header_titulo}}", WebUtility.HtmlEncode(context.Copy?.Titulo ?? string.Empty));
        html = html.Replace("{{header_titulo_cor}}", corTitulo);
        html = html.Replace("{{header_titulo_align}}", alinhamento);
        html = html.Replace("{{header_titulo_padding}}",
            alinhamento == "right" ? "padding: 0 0 0 22px;" : "padding: 0 22px 0 0;");
        html = html.Replace("{{header_hero_src}}", WebUtility.HtmlEncode(context.HeroSrc ?? string.Empty));
        html = html.Replace("{{header_moldura_cor}}", corMoldura);
        html = html.Replace("{{regua_html}}", ReguaHtmlBuilder.Construir(context.Estrategia, coresFundo, reguaComponente));
        return html;
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

    /// <summary>Cor do titulo do header: declarada na fase ou escolhida por contraste.</summary>
    internal static string CorTitulo(FaseEstrategia? fase, IReadOnlyCollection<string>? coresFundo = null)
    {
        return CorHex.EscolherTexto(coresFundo ?? CoresGradiente(fase), fase?.CorTextoHeader);
    }

    /// <summary>Cor da moldura atras do hero: declarada na fase ou branco validado por contraste.</summary>
    internal static string CorMoldura(FaseEstrategia? fase, IReadOnlyCollection<string>? coresFundo = null)
    {
        return CorHex.EscolherVisivel(
            coresFundo ?? CoresGradiente(fase),
            fase?.CorMolduraHeader ?? CorHex.MolduraPadrao);
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

    /// <summary>Cores efetivamente usadas no gradiente (no maximo os dois primeiros stops).</summary>
    internal static IReadOnlyList<string> CoresGradiente(FaseEstrategia? fase) => CoresFase.Gradiente(fase);

    /// <summary>
    /// Extrai as cores de identidade da fase (linhas que nao sao os gradientes fixos da marca).
    /// Mantido como API do builder; a regra vive em <see cref="CoresFase"/>.
    /// </summary>
    internal static IReadOnlyList<string> ExtrairCoresFase(FaseEstrategia? fase) => CoresFase.Identidade(fase);
}
