using System.Text.Json;
using System.Text.RegularExpressions;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepQaEmail : IPipelineStep
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

        if (!string.IsNullOrEmpty(html))
        {
            var placeholders = ExtrairPlaceholders(html);
            if (placeholders.Count > 0)
            {
                prompt += $"\n## Placeholders do ESP (ESPERADOS - nao sao defeitos)\n";
                prompt += $"Os seguintes tokens %%...%% no HTML sao variaveis preenchidas pelo ESP (Email Service Provider) no momento do envio:\n";
                prompt += $"{string.Join(", ", placeholders.Select(p => $"%%{p}%%"))}\n";
                prompt += $"Nao reprove por link de CTA nao funcional ou dados concretos ausentes quando representados por esses placeholders.\n";
            }
        }

        var briefTokens = ExtrairPlaceholders(brief.Link ?? string.Empty)
            .Concat(ExtrairPlaceholders(context.MensagemOriginal ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (briefTokens.Count > 0)
        {
            prompt += $"\n## Placeholders do briefing (CANONICOS)\n";
            prompt += $"O briefing especifica os seguintes tokens: {string.Join(", ", briefTokens.Select(t => $"%%{t}%%"))}\n";
            prompt += $"Estes tokens devem aparecer no HTML EXATAMENTE com o mesmo nome. Divergencia de nome (ex: %%CONFIRM_LINK%% no lugar de %%LINKASSEMBLEIA%%) e defeito critico.\n";
        }

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

    internal static (bool aprovado, string? feedback, string? stepAlvo) ParseQaResult(string resposta)
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

            stepAlvo = NormalizarStepAlvo(stepAlvo);

            if (!aprovado && string.IsNullOrEmpty(stepAlvo))
                stepAlvo = "copy";

            return (aprovado, feedback, stepAlvo);
        }
        catch
        {
            return (false, "QA: falha no parse JSON", "copy");
        }
    }

    private static string NormalizarStepAlvo(string? stepAlvo)
    {
        if (string.IsNullOrWhiteSpace(stepAlvo))
            return "copy";

        var normalized = stepAlvo.Trim().ToLowerInvariant();

        if (normalized.Contains("hero") || normalized.Contains("imagem"))
            return "hero";
        if (normalized.Contains("diagramacao") || normalized.Contains("diagram"))
            return "diagramacao";
        if (normalized.Contains("copy") || normalized.Contains("texto"))
            return "copy";

        return "copy";
    }

    internal static IReadOnlyList<string> ExtrairPlaceholders(string html)
    {
        if (string.IsNullOrEmpty(html))
            return Array.Empty<string>();

        return PlaceholderRegex()
            .Matches(html)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"%%([A-Za-z0-9_]+)%%")]
    private static partial Regex PlaceholderRegex();
}
