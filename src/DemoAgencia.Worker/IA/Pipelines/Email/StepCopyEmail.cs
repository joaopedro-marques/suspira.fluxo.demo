using System.Text.Json;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepCopyEmail : IPipelineStep
{
    public string Nome => "copy";

    private const string ModeloAlvo = "qwen/qwen3.7-plus";
    private const double Temperatura = 0.8;
    private const int MaxTokens = 2000;

    private const string Persona = """
        Voce e um redator especialista em email marketing. Sua funcao e criar copy persuasiva para emails.

        ## Diretrizes
        - Use tecnicas de copywriting (AIDA, PAS)
        - Considere o publico-alvo
        - Priorize clareza e impacto
        - Inclua CTAs claros
        - Mantenha o tom consistente com a marca

        ## Formato de Resposta (JSON OBRIGATORIO)
        Responda APENAS com JSON valido:
        {
            "assunto": "Linha de assunto do email (max 60 chars)",
            "preheader": "Texto de preheader (max 100 chars, complementa o assunto)",
            "titulo": "Titulo principal do email (H1)",
            "saudacao": "Saudacao inicial (ex: Ola, [Nome]!)",
            "corpo": "Corpo do email em HTML (use <p>, <strong>, <em>, listas <ul>/<li>)",
            "cta_texto": "Texto do botao de call-to-action",
            "cta_link": "URL do link do CTA",
            "rodape": "Texto do rodape (informacoes legais, unsubscribe)"
        }

        - assunto: curto, direto, que desperte curiosidade ou urgencia
        - preheader: complementa o assunto, aparece na preview do email
        - titulo: destaque principal do email
        - saudacao: abertura pessoal
        - corpo: HTML com paragrafos, formatacao, listas quando apropriado
        - cta_texto: acao clara (ex: "Compre agora", "Saiba mais", "Baixe o ebook")
        - cta_link: URL completa (https://...)
        - rodape: informacoes legais, como cancelar inscricao

        Responda APENAS com JSON valido, sem explicacoes adicionais.
        """;

    private readonly IServicoChat _servicoChat;

    public StepCopyEmail(IServicoChat servicoChat)
    {
        _servicoChat = servicoChat;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = MontarPrompt(context);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            context.ChatId,
            Persona,
            ModeloAlvo,
            prompt,
            "email_copy",
            temperature: Temperatura,
            maxTokens: MaxTokens,
            ct: ct);

        var slots = ParseCopySlots(resposta);
        if (slots == null)
            throw new InvalidOperationException($"StepCopyEmail: resposta invalida do LLM. Resposta: {resposta?[..Math.Min(200, resposta?.Length ?? 0)]}");

        context.Copy = slots;
        return context;
    }

    private static string MontarPrompt(PipelineContext context)
    {
        var brief = context.Brief;
        var marca = context.Marca;

        var prompt = $"## Briefing\n";
        if (!string.IsNullOrEmpty(brief.Objetivo))
            prompt += $"Objetivo: {brief.Objetivo}\n";
        if (!string.IsNullOrEmpty(brief.Publico))
            prompt += $"Publico-alvo: {brief.Publico}\n";
        if (!string.IsNullOrEmpty(brief.Oferta))
            prompt += $"Oferta/produto: {brief.Oferta}\n";
        if (!string.IsNullOrEmpty(brief.Link))
            prompt += $"Link do CTA: {brief.Link}\n";
        if (brief.Restricoes.Count > 0)
            prompt += $"Restricoes: {string.Join(", ", brief.Restricoes)}\n";

        if (marca != null)
        {
            if (!string.IsNullOrEmpty(marca.TomDeVoz))
                prompt += $"\n## Tom de voz da marca\n{marca.TomDeVoz}\n";
            if (!string.IsNullOrEmpty(marca.Cores))
                prompt += $"\n## Paleta de cores\n{marca.Cores}\n";
        }

        return prompt;
    }

    private static CopyEmailSlots? ParseCopySlots(string resposta)
    {
        var json = OrquestradorLoop.ParserDecisao.ExtrairJson(resposta);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var assunto = root.TryGetProperty("assunto", out var a) ? a.GetString() : null;
            var preheader = root.TryGetProperty("preheader", out var p) ? p.GetString() : null;
            var titulo = root.TryGetProperty("titulo", out var t) ? t.GetString() : null;
            var saudacao = root.TryGetProperty("saudacao", out var s) ? s.GetString() : null;
            var corpo = root.TryGetProperty("corpo", out var c) ? c.GetString() : null;
            var ctaTexto = root.TryGetProperty("cta_texto", out var ct) ? ct.GetString() : null;
            var ctaLink = root.TryGetProperty("cta_link", out var cl) ? cl.GetString() : null;
            var rodape = root.TryGetProperty("rodape", out var r) ? r.GetString() : null;

            if (string.IsNullOrEmpty(assunto) || string.IsNullOrEmpty(corpo))
                return null;

            return new CopyEmailSlots(
                assunto ?? "",
                preheader ?? "",
                titulo ?? "",
                saudacao ?? "",
                corpo,
                ctaTexto ?? "",
                ctaLink ?? "",
                rodape ?? "");
        }
        catch
        {
            return null;
        }
    }
}
