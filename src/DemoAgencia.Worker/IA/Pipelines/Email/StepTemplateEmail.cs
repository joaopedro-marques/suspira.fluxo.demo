using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;
using Microsoft.Extensions.Logging;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepTemplateEmail : IPipelineStep
{
    public string Nome => "template";

    private readonly ITemplateCatalogo _catalogo;
    private readonly IReferenciasCliente _referencias;
    private readonly ILogger<StepTemplateEmail> _logger;

    public StepTemplateEmail(ITemplateCatalogo catalogo, IReferenciasCliente referencias, ILogger<StepTemplateEmail> logger)
    {
        _catalogo = catalogo;
        _referencias = referencias;
        _logger = logger;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var copy = context.Copy ?? throw new InvalidOperationException("Step copy nao executado antes do template.");

        var templateContent = !string.IsNullOrEmpty(context.TemplateId)
            ? _catalogo.Obter(context.TemplateId)
            : null;

        var html = templateContent ?? _catalogo.Default;

        if (string.IsNullOrEmpty(html))
            throw new InvalidOperationException("Template de email nao encontrado. Verifique se ha templates em Assets/referencias/templates/.");

        // Inject footers before extracting FixedAssetRefs so footer images are included
        html = InjectFooters(html, context.Cliente);

        context.FixedAssetRefs = ExtrairFixedAssetRefs(html);

        html = html.Replace("{{assunto}}", HtmlEncode(copy.Assunto));
        html = html.Replace("{{preheader}}", HtmlEncode(copy.Preheader));
        html = html.Replace("{{titulo}}", HtmlEncode(copy.Titulo));
        html = html.Replace("{{corpo}}", copy.Corpo);
        html = html.Replace("{{cta_link}}", HtmlEncode(copy.CtaLink));
        html = html.Replace("{{cta_texto}}", HtmlEncode(copy.CtaTexto));
        html = html.Replace("{{logo_src}}", HtmlEncode(context.LogoSrc ?? ""));

        var heroSectionTemplate = _referencias.ObterHeroSectionTemplate();
        var headerConfig = context.Estrategia?.Header;
        var headerSectionTemplate = _referencias.ObterHeaderSectionTemplate(context.Cliente);
        var reguaSectionTemplate = _referencias.ObterReguaSectionTemplate(context.Cliente);
        var headerRenderizado = false;
        var requerHero = headerConfig?.RequerHero ?? true;

        if (html.Contains("{{header_section}}", StringComparison.Ordinal))
        {
            if ((!requerHero || !string.IsNullOrEmpty(context.HeroSrc)) && !string.IsNullOrEmpty(headerSectionTemplate))
            {
                var invertido = ResolverLayoutInvertido(context, headerConfig);
                var headerHtml = HeaderEmailBuilder.Construir(headerSectionTemplate, context, invertido, reguaSectionTemplate);
                html = html.Replace("{{header_section}}", headerHtml);
                headerRenderizado = true;
            }
            else
            {
                html = html.Replace("{{header_section}}", "");
            }
        }

        if (!headerRenderizado && !string.IsNullOrEmpty(context.HeroSrc) && !string.IsNullOrEmpty(heroSectionTemplate))
        {
            var heroHtml = heroSectionTemplate.Replace("{{hero_src}}", HtmlEncode(context.HeroSrc));
            html = html.Replace("{{hero_section}}", heroHtml);
        }
        else
        {
            html = html.Replace("{{hero_section}}", "");
        }

        var bannerSectionTemplate = _referencias.ObterBannerSectionTemplate();
        if (!string.IsNullOrEmpty(context.BannerSrc) && !string.IsNullOrEmpty(bannerSectionTemplate))
        {
            var bannerHtml = bannerSectionTemplate.Replace("{{banner_src}}", HtmlEncode(context.BannerSrc));
            html = html.Replace("{{banner_section}}", bannerHtml);
        }
        else
        {
            html = html.Replace("{{banner_section}}", "");
        }

        context.Html = html;
        context.Resultado.RespostaFinal = html;

        return Task.FromResult(context);
    }

    /// <summary>
    /// Decide (uma unica vez por entrega) se o hero vem antes do titulo. A ordem e regida
    /// por <see cref="HeaderConfig.SortearOrdem"/> no asset do cliente.
    /// </summary>
    internal static bool ResolverLayoutInvertido(PipelineContext context, HeaderConfig? header = null)
    {
        if (context.HeaderLayoutInvertido is { } definido)
            return definido;

        header ??= context.Estrategia?.Header;
        var sortear = header?.SortearOrdem ?? true;
        var invertido = sortear && Random.Shared.Next(2) == 0;
        context.HeaderLayoutInvertido = invertido;
        return invertido;
    }

    internal string InjectFooters(string html, string? cliente)
    {
        if (string.IsNullOrEmpty(cliente))
        {
            // Remove all footer placeholders if no client
            html = FooterPlaceholderRegex().Replace(html, "");
            return html;
        }

        html = FooterPlaceholderRegex().Replace(html, match =>
        {
            var nome = match.Groups["nome"].Value;
            var footerHtml = _referencias.ObterFooterHtml(cliente, string.IsNullOrEmpty(nome) ? null : nome);
            
            if (footerHtml == null)
            {
                var footerId = string.IsNullOrEmpty(nome) ? "default" : nome;
                _logger.LogWarning("Footer '{FooterId}' not found for client '{Cliente}'. Removing placeholder.", footerId, cliente);
                return "";
            }
            
            return footerHtml;
        });

        return html;
    }

    internal static IReadOnlyList<string> ExtrairFixedAssetRefs(string templateHtml)
    {
        var matches = AssetsSrcRegex().Matches(templateHtml);
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                refs.Add(match.Groups[1].Value);
        }
        return refs.ToList();
    }

    private static string HtmlEncode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    [GeneratedRegex(@"{{footer(?::(?<nome>[a-zA-Z0-9_]+))?}}", RegexOptions.Compiled)]
    private static partial Regex FooterPlaceholderRegex();

    [GeneratedRegex(@"src=""assets/([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AssetsSrcRegex();
}
