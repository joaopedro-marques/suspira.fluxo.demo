using System.Text.Json;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepQaEmail : IPipelineStep
{
    public string Nome => "qa";

    private const string ModeloAlvo = "deepseek/deepseek-r1-0528";
    private const double Temperatura = 0.3;
    private const int MaxTokens = 1000;

    private const string Persona = """
        Voce e um revisor critico independente especializado em email marketing. Sua funcao e avaliar emails HTML e garantir que atendem ao briefing.

        ## Criterios de Avaliacao
        1. **Aderencia ao briefing**: O email atende ao objetivo, publico e oferta especificados?
        2. **Qualidade da copy**: Assunto desperta interesse? Preheader complementa? Titulo e claro? Corpo e persuasivo e bem estruturado?
        3. **CTA eficaz**: Texto do CTA e claro e acionavel? Link esta correto?
        4. **Consistencia com a marca**: Tom de voz alinhado? Cores apropriadas (se mencionado)?
        5. **HTML valido**: Estrutura table-based, CSS inline, sem divs para layout, ghost tables para Outlook?
        6. **Completude**: Todos os slots preenchidos? Rodape com informacoes legais?
        7. **Imagem hero (se presente)**: Relevante para o conteudo? Qualidade profissional?

        ## Formato de Resposta (JSON OBRIGATORIO)
        Responda APENAS com JSON valido:

        Se aprovado:
        {"aprovado": true, "feedback": "Breve justificativa da aprovacao"}

        Se reprovado:
        {"aprovado": false, "feedback": "Instrucoes claras e especificas para correcao", "step_alvo": "copy ou hero"}

        - aprovado: true se o email atende aos criterios minimos, false caso contrario
        - feedback: justificativa detalhada (se aprovado) ou instrucoes acionaveis (se reprovado)
        - step_alvo: (apenas se reprovado) qual step refazer:
          - "copy": se o problema e na copy (assunto, titulo, corpo, CTA texto, rodape)
          - "hero": se o problema e na imagem hero (irrelevante, baixa qualidade, nao corresponde ao briefing)

        ## Regras
        - Seja criterioso mas justo: aprove quando os criterios minimos sao atendidos
        - Ao reprovar, o feedback deve ser acionavel: diga exatamente o que precisa ser corrigido
        - NUNCA aprove um email que nao atenda ao briefing original
        - Problemas de estrutura HTML (table-based, CSS inline, etc.) sao responsabilidade do template, nao reprove por isso
        - Responda SEMPRE em JSON valido
        """;

    private readonly IServicoChat _servicoChat;

    public StepQaEmail(IServicoChat servicoChat)
    {
        _servicoChat = servicoChat;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = MontarPromptQa(context);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            context.ChatId,
            Persona,
            ModeloAlvo,
            prompt,
            "email_qa",
            temperature: Temperatura,
            maxTokens: MaxTokens,
            ct: ct);

        var (aprovado, feedback, stepAlvo) = ParseQaResult(resposta);

        context.QaAprovado = aprovado;
        context.QaFeedback = feedback;
        context.QaStepAlvo = stepAlvo;

        return context;
    }

    private static string MontarPromptQa(PipelineContext context)
    {
        var brief = context.Brief;
        var copy = context.Copy;
        var html = context.Html;

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
