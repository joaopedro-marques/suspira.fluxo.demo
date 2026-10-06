using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepCopyEmail : IPipelineStep
{
    public string Nome => "copy";

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;
    private readonly ILogger<StepCopyEmail> _logger;

    public StepCopyEmail(AgenteDefinicao agente, IServicoChat servicoChat, ILogger<StepCopyEmail> logger)
    {
        _agente = agente;
        _servicoChat = servicoChat;
        _logger = logger;
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

        if (!string.IsNullOrEmpty(context.Brief.Link) && slots.CtaLink != context.Brief.Link)
        {
            _logger.LogWarning("StepCopyEmail: override CTA link. Redator retornou '{Redator}', briefing especificou '{Brief}'.",
                slots.CtaLink, context.Brief.Link);
            slots = slots with { CtaLink = context.Brief.Link };
        }

        context.Copy = slots;
        return context;
    }

    internal static string MontarPrompt(PipelineContext context)
    {
        var brief = context.Brief;
        var marca = context.Marca;
        var estrategia = context.Estrategia;

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

        if (estrategia?.FaseDados != null)
        {
            prompt += $"\n## Estrategia de jornada - fase: {estrategia.Fase}\n";
            if (!string.IsNullOrEmpty(estrategia.SubJornada))
                prompt += $"Sub-jornada: {estrategia.SubJornada}\n";
            if (!string.IsNullOrEmpty(estrategia.FaseDados.CorPrincipal))
                prompt += $"Cor da fase: {estrategia.FaseDados.CorPrincipal}\n";
            if (!string.IsNullOrEmpty(estrategia.FaseDados.DescricaoCor))
                prompt += $"Sentimento da cor: {estrategia.FaseDados.DescricaoCor}\n";
            if (estrategia.FaseDados.Temas.Count > 0)
                prompt += $"Temas: {string.Join(", ", estrategia.FaseDados.Temas)}\n";
        }

        if (estrategia?.MapaEmocional.Count > 0)
        {
            var sentimentos = estrategia.MapaEmocional.SelectMany(e => e.Sentimentos).Distinct();
            if (sentimentos.Any())
                prompt += $"\n## Sentimentos-alvo na jornada\n{string.Join(", ", sentimentos)}\n";
        }

        if (estrategia != null)
        {
            if (estrategia.Satisfacoes.Count > 0 || estrategia.Insatisfacoes.Count > 0)
            {
                prompt += $"\n## Guia de satisfacoes e insatisfacoes\n";
                if (estrategia.Satisfacoes.Count > 0)
                {
                    var itens = estrategia.Satisfacoes.SelectMany(c => c.Itens).Distinct();
                    prompt += $"Pontos de satisfacao a reforcar: {string.Join(", ", itens)}\n";
                }
                if (estrategia.Insatisfacoes.Count > 0)
                {
                    var itens = estrategia.Insatisfacoes.SelectMany(c => c.Itens).Distinct();
                    prompt += $"Pontos de insatisfacao a mitigar: {string.Join(", ", itens)}\n";
                }
            }
        }

        if (context.Refacoes > 0 && !string.IsNullOrEmpty(context.QaFeedback))
        {
            prompt += $"\n## ⚠️ REVISAO NECESSARIA (refacao {context.Refacoes})\n";
            prompt += $"O QA reprovou a versao anterior. Corrija os seguintes problemas:\n";
            prompt += $"{context.QaFeedback}\n";

            if (context.Copy != null)
            {
                prompt += $"\n## Copy anterior (para referencia)\n";
                prompt += $"Assunto: {context.Copy.Assunto}\n";
                prompt += $"Titulo: {context.Copy.Titulo}\n";
                prompt += $"CTA: {context.Copy.CtaTexto}\n";
            }
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
            var corpo = root.TryGetProperty("corpo", out var c) ? c.GetString() : null;
            var ctaTexto = root.TryGetProperty("cta_texto", out var ct) ? ct.GetString() : null;
            var ctaLink = root.TryGetProperty("cta_link", out var cl) ? cl.GetString() : null;

            if (string.IsNullOrEmpty(assunto) || string.IsNullOrEmpty(corpo))
                return null;

            return new CopyEmailSlots(
                assunto ?? "",
                preheader ?? "",
                titulo ?? "",
                corpo,
                ctaTexto ?? "",
                ctaLink ?? "");
        }
        catch
        {
            return null;
        }
    }
}
