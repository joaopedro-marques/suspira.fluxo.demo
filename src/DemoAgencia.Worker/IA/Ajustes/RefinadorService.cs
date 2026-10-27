using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Classifica o feedback do cliente sobre um email entregue e determina a menor
/// etapa da pipeline que precisa ser refeita para corrigir o problema.
/// </summary>
public class RefinadorService
{
    private const int MaxCharsHtml = 8000;

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;
    private readonly ITemplateCatalogo _templateCatalogo;
    private readonly ILogger<RefinadorService> _logger;

    public RefinadorService(
        AgenteDefinicao agente,
        IServicoChat servicoChat,
        ITemplateCatalogo templateCatalogo,
        ILogger<RefinadorService> logger)
    {
        _agente = agente;
        _servicoChat = servicoChat;
        _templateCatalogo = templateCatalogo;
        _logger = logger;
    }

    public virtual async Task<PlanoAjuste?> ClassificarAsync(
        PipelineContext contexto,
        string feedback,
        CancellationToken ct = default)
    {
        var templates = ListarTemplates(contexto.Cliente);
        var prompt = MontarPrompt(contexto, feedback, templates);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            contexto.ChatId,
            _agente.Persona,
            _agente.Modelo,
            prompt,
            "email_refinador",
            temperature: _agente.Temperatura,
            maxTokens: _agente.MaxTokens,
            ct: ct);

        var plano = ParseResposta(resposta, feedback, templates);
        if (plano != null)
            return plano;

        _logger.LogWarning("Refinador retornou resposta invalida. Tentando retry. Resposta: {Resposta}",
            resposta?[..Math.Min(200, resposta?.Length ?? 0)]);

        var retryPrompt = prompt + """

            ATENCAO: Responda APENAS com JSON valido na raiz, sem fences markdown, sem explicacoes.
            Campos obrigatorios: "step_alvo" (copy | diagramacao | hero | template) e "instrucoes".
            """;
        var retryResposta = await _servicoChat.ChamarAgenteAsync(
            contexto.ChatId,
            _agente.Persona,
            _agente.Modelo,
            retryPrompt,
            "email_refinador_retry",
            temperature: _agente.Temperatura,
            maxTokens: _agente.MaxTokens,
            ct: ct);

        plano = ParseResposta(retryResposta, feedback, templates);
        if (plano == null)
            _logger.LogWarning("Refinador retornou JSON invalido 2x. Desistindo.");

        return plano;
    }

    internal IReadOnlyList<string> ListarTemplates(string? cliente)
    {
        return _templateCatalogo.ListarIds(cliente) ?? Array.Empty<string>();
    }

    internal static string MontarPrompt(PipelineContext contexto, string feedback, IReadOnlyList<string> templates)
    {
        var brief = contexto.Brief;
        var copy = contexto.Copy;
        var estrategia = contexto.Estrategia;

        var prompt = new StringBuilder();
        prompt.AppendLine("## Email ja entregue ao cliente");
        prompt.AppendLine("Classifique o feedback abaixo e diga qual e a MENOR etapa da pipeline que, refeita, corrige o problema.");
        prompt.AppendLine();

        prompt.AppendLine("### Briefing original");
        prompt.AppendLine($"Mensagem do cliente: {contexto.MensagemOriginal}");
        if (!string.IsNullOrEmpty(brief.Objetivo))
            prompt.AppendLine($"Objetivo: {brief.Objetivo}");
        if (!string.IsNullOrEmpty(brief.Publico))
            prompt.AppendLine($"Publico-alvo: {brief.Publico}");
        if (!string.IsNullOrEmpty(brief.Oferta))
            prompt.AppendLine($"Oferta/produto: {brief.Oferta}");
        if (!string.IsNullOrEmpty(brief.Tom))
            prompt.AppendLine($"Tom desejado: {brief.Tom}");
        if (!string.IsNullOrEmpty(brief.Link))
            prompt.AppendLine($"Link do CTA: {brief.Link}");
        if (brief.Restricoes.Count > 0)
            prompt.AppendLine($"Restricoes: {string.Join(", ", brief.Restricoes)}");
        prompt.AppendLine();

        if (copy != null)
        {
            prompt.AppendLine("### Copy atual");
            prompt.AppendLine($"Assunto: {copy.Assunto}");
            prompt.AppendLine($"Preheader: {copy.Preheader}");
            prompt.AppendLine($"Titulo: {copy.Titulo}");
            prompt.AppendLine($"CTA: {copy.CtaTexto} -> {copy.CtaLink}");
            prompt.AppendLine();
        }

        if (estrategia != null)
        {
            prompt.AppendLine("### Estrategia de jornada");
            prompt.AppendLine($"Fase: {estrategia.Fase}");
            if (!string.IsNullOrEmpty(estrategia.SubJornada))
                prompt.AppendLine($"Sub-jornada: {estrategia.SubJornada}");
            if (!string.IsNullOrEmpty(estrategia.TemaSelecionado))
                prompt.AppendLine($"Tema: {estrategia.TemaSelecionado}");
            prompt.AppendLine();
        }

        prompt.AppendLine("### Midia da entrega");
        prompt.AppendLine(!string.IsNullOrEmpty(contexto.HeroSrc)
            ? $"Imagem hero presente: {contexto.HeroSrc}"
            : "Sem imagem hero gerada");
        prompt.AppendLine(!string.IsNullOrEmpty(contexto.BannerSrc)
            ? $"Banner de referencia presente: {contexto.BannerSrc}"
            : "Sem banner de referencia");
        prompt.AppendLine();

        prompt.AppendLine("### Template");
        prompt.AppendLine($"Template atual: {contexto.TemplateId ?? "(nao informado)"}");
        if (templates.Count > 0)
        {
            prompt.AppendLine("Templates disponiveis:");
            foreach (var template in templates)
                prompt.AppendLine($"- {template}");
        }
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(contexto.Html))
        {
            var html = contexto.Html.Length > MaxCharsHtml
                ? contexto.Html[..MaxCharsHtml] + "\n[HTML truncado]"
                : contexto.Html;
            prompt.AppendLine("### HTML atual (trecho)");
            prompt.AppendLine("```");
            prompt.AppendLine(html);
            prompt.AppendLine("```");
            prompt.AppendLine();
        }

        prompt.AppendLine("## Feedback do cliente");
        prompt.AppendLine(feedback.Trim());
        prompt.AppendLine();

        prompt.AppendLine("## Regras de classificacao");
        prompt.AppendLine("- copy: problema de redacao (assunto, preheader, titulo, corpo, texto do CTA, tom).");
        prompt.AppendLine("- diagramacao: problema visual do corpo (layout das secoes, cores, contraste, organizacao, icones).");
        prompt.AppendLine("- hero: problema na imagem principal/banner (irrelevante, generica, nao corresponde ao tema).");
        prompt.AppendLine("- template: problema na estrutura fixa do email (header, rodape, logo, slots) ou pedido explicito de trocar o template.");
        prompt.AppendLine("- Escolha sempre a MENOR etapa capaz de resolver; nao proponha refazer o email inteiro.");
        prompt.AppendLine("- instrucoes devem ser acionaveis e autocontidas, dizendo exatamente o que mudar e o que preservar.");
        prompt.AppendLine("- Se step_alvo for template, informe template_id escolhido entre os disponiveis (senao omita).");

        return prompt.ToString();
    }

    internal static PlanoAjuste? ParseResposta(
        string? resposta,
        string feedbackOriginal,
        IReadOnlyList<string>? templates = null)
    {
        var json = JsonHelper.ExtrairJson(resposta ?? string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var stepBruto = root.TryGetProperty("step_alvo", out var stepEl) ? stepEl.GetString() : null;
            var stepAlvo = StepAlvoNormalizer.NormalizarAjuste(stepBruto) ?? "copy";

            var instrucoes = root.TryGetProperty("instrucoes", out var instrEl) ? instrEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(instrucoes))
                instrucoes = feedbackOriginal;

            var templateId = root.TryGetProperty("template_id", out var templateEl) ? templateEl.GetString() : null;
            if (stepAlvo == "template")
            {
                templateId = ValidarTemplate(templateId, templates);
            }
            else
            {
                templateId = null;
            }

            return new PlanoAjuste(stepAlvo, instrucoes!.Trim(), feedbackOriginal, templateId);
        }
        catch
        {
            return null;
        }
    }

    private static string? ValidarTemplate(string? templateId, IReadOnlyList<string>? templates)
    {
        if (string.IsNullOrWhiteSpace(templateId) || templates == null || templates.Count == 0)
            return null;

        return templates.FirstOrDefault(t => t.Equals(templateId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
