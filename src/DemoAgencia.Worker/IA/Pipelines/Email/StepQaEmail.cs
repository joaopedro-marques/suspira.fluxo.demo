using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepQaEmail : IPipelineStep
{
    public string Nome => "qa";

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;

    public StepQaEmail(AgenteDefinicao agente, IServicoChat servicoChat)
    {
        _agente = agente;
        _servicoChat = servicoChat;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = MontarPromptQa(context);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            context.ChatId,
            _agente.Persona,
            _agente.Modelo,
            prompt,
            "email_qa",
            temperature: _agente.Temperatura,
            maxTokens: _agente.MaxTokens,
            ct: ct);

        var (aprovado, feedback, stepAlvo) = ParseQaResult(resposta);

        context.QaAprovado = aprovado;
        context.QaFeedback = feedback;
        context.QaStepAlvo = stepAlvo;

        return context;
    }

    internal static string MontarPromptQa(PipelineContext context)
    {
        var brief = context.Brief;
        var copy = context.Copy;
        var html = context.Html;
        var estrategia = context.Estrategia;

        var prompt = $"## Briefing original\n";
        prompt += $"Mensagem do usuario: {context.MensagemOriginal}\n\n";

        if (!string.IsNullOrEmpty(brief.Objetivo))
            prompt += $"Objetivo: {brief.Objetivo}\n";
        if (!string.IsNullOrEmpty(brief.Publico))
            prompt += $"Publico-alvo: {brief.Publico}\n";
        if (!string.IsNullOrEmpty(brief.Oferta))
            prompt += $"Oferta/produto: {brief.Oferta}\n";
        if (!string.IsNullOrEmpty(brief.Tom))
            prompt += $"Tom desejado: {brief.Tom}\n";
        if (!string.IsNullOrEmpty(brief.Link))
            prompt += $"Link do CTA: {brief.Link}\n";
        if (brief.Restricoes.Count > 0)
            prompt += $"Restricoes: {string.Join(", ", brief.Restricoes)}\n";

        if (copy != null)
        {
            prompt += $"\n## Copy gerada\n";
            prompt += $"Assunto: {copy.Assunto}\n";
            prompt += $"Preheader: {copy.Preheader}\n";
            prompt += $"Titulo: {copy.Titulo}\n";
            prompt += $"CTA: {copy.CtaTexto} → {copy.CtaLink}\n";
        }

        if (!string.IsNullOrEmpty(html))
        {
            prompt += $"\n## HTML do email\n```\n{html}\n```\n";
        }

        prompt += $"\n## Imagem hero\n";
        if (!string.IsNullOrEmpty(context.HeroSrc))
            prompt += $"Presente: {context.HeroSrc}\n";
        else
            prompt += $"Ausente (sem brief de imagem hero)\n";

        prompt += $"\n## Banner de referencia\n";
        if (!string.IsNullOrEmpty(context.BannerSrc))
            prompt += $"Presente: {context.BannerSrc}\n";
        else
            prompt += $"Ausente (nenhum banner afim a etapa)\n";

        if (estrategia?.FaseDados != null)
        {
            prompt += $"\n## Estrategia de jornada\n";
            prompt += $"Fase: {estrategia.Fase}\n";
            if (!string.IsNullOrEmpty(estrategia.SubJornada))
                prompt += $"Sub-jornada: {estrategia.SubJornada}\n";
            if (estrategia.FaseDados.Temas.Count > 0)
                prompt += $"Temas esperados: {string.Join(", ", estrategia.FaseDados.Temas)}\n";
            if (estrategia.MapaEmocional.Count > 0)
            {
                var sentimentos = estrategia.MapaEmocional.SelectMany(e => e.Sentimentos).Distinct();
                if (sentimentos.Any())
                    prompt += $"Sentimentos esperados: {string.Join(", ", sentimentos)}\n";
            }
            prompt += "Avalie se a copy e a imagem estao alinhadas com a fase, temas e sentimentos da jornada.\n";
        }

        return prompt;
    }

    private static (bool aprovado, string? feedback, string? stepAlvo) ParseQaResult(string resposta)
    {
        var json = JsonHelper.ExtrairJson(resposta);
        if (string.IsNullOrEmpty(json))
            return (false, "QA: resposta invalida do LLM", "copy");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var aprovado = root.TryGetProperty("aprovado", out var apEl) && apEl.GetBoolean();
            var feedback = root.TryGetProperty("feedback", out var fbEl) ? fbEl.GetString() : null;
            var stepAlvo = root.TryGetProperty("step_alvo", out var stEl) ? stEl.GetString() : "copy";

            if (!aprovado && string.IsNullOrEmpty(stepAlvo))
                stepAlvo = "copy";

            return (aprovado, feedback, stepAlvo);
        }
        catch
        {
            return (false, "QA: falha no parse JSON", "copy");
        }
    }
}
