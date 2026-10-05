using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepCopyEmail : IPipelineStep
{
    public string Nome => "copy";

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;

    public StepCopyEmail(AgenteDefinicao agente, IServicoChat servicoChat)
    {
        _agente = agente;
        _servicoChat = servicoChat;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = MontarPrompt(context);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            context.ChatId,
            _agente.Persona,
            _agente.Modelo,
            prompt,
            "email_copy",
            temperature: _agente.Temperatura,
            maxTokens: _agente.MaxTokens,
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
        var json = JsonHelper.ExtrairJson(resposta);
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
