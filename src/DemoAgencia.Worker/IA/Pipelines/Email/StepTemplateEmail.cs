using System.Net;
using System.Text;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepTemplateEmail : IPipelineStep
{
    public string Nome => "template";

    private readonly string _template;
    private readonly string _heroSectionTemplate;

    public StepTemplateEmail(string templateContent, string heroSectionTemplate)
    {
        _template = templateContent;
        _heroSectionTemplate = heroSectionTemplate;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var copy = context.Copy ?? throw new InvalidOperationException("Step copy nao executado antes do template.");

        var html = _template;

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

        context.Html = html;
        context.Resultado.RespostaFinal = html;

        return Task.FromResult(context);
    }

    private static string HtmlEncode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
