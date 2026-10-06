using System.Net;
using System.Text;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepTemplateEmail : IPipelineStep
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

        html = html.Replace("{{assunto}}", HtmlEncode(copy.Assunto));
        html = html.Replace("{{preheader}}", HtmlEncode(copy.Preheader));
        html = html.Replace("{{titulo}}", HtmlEncode(copy.Titulo));
        html = html.Replace("{{saudacao}}", HtmlEncode(copy.Saudacao));
        html = html.Replace("{{corpo}}", copy.Corpo);
        html = html.Replace("{{cta_link}}", HtmlEncode(copy.CtaLink));
        html = html.Replace("{{cta_texto}}", HtmlEncode(copy.CtaTexto));
        html = html.Replace("{{logo_src}}", HtmlEncode(context.LogoSrc ?? ""));
        html = html.Replace("{{rodape}}", HtmlEncode(copy.Rodape));

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

    private static string HtmlEncode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
