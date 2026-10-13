using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepTemplateEmail : IPipelineStep
{
    public string Nome => "template";

    private readonly ITemplateCatalogo _catalogo;
    private readonly string _heroSectionTemplate;
    private readonly string _bannerSectionTemplate;

    public StepTemplateEmail(ITemplateCatalogo catalogo, string heroSectionTemplate, string bannerSectionTemplate)
    {
        _catalogo = catalogo;
        _heroSectionTemplate = heroSectionTemplate;
        _bannerSectionTemplate = bannerSectionTemplate;
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

        context.FixedAssetRefs = ExtrairFixedAssetRefs(html);

        html = html.Replace("{{assunto}}", HtmlEncode(copy.Assunto));
        html = html.Replace("{{preheader}}", HtmlEncode(copy.Preheader));
        html = html.Replace("{{titulo}}", HtmlEncode(copy.Titulo));
        html = html.Replace("{{corpo}}", copy.Corpo);
        html = html.Replace("{{cta_link}}", HtmlEncode(copy.CtaLink));
        html = html.Replace("{{cta_texto}}", HtmlEncode(copy.CtaTexto));
        html = html.Replace("{{logo_src}}", HtmlEncode(context.LogoSrc ?? ""));

        if (!string.IsNullOrEmpty(context.HeroSrc))
        {
            var heroHtml = _heroSectionTemplate.Replace("{{hero_src}}", HtmlEncode(context.HeroSrc));
            html = html.Replace("{{hero_section}}", heroHtml);
        }
        else
        {
            html = html.Replace("{{hero_section}}", "");
        }

        if (!string.IsNullOrEmpty(context.BannerSrc))
        {
            var bannerHtml = _bannerSectionTemplate.Replace("{{banner_src}}", HtmlEncode(context.BannerSrc));
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

    [GeneratedRegex(@"src=""assets/([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AssetsSrcRegex();
}
