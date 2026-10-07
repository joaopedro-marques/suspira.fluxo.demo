using System.Text.RegularExpressions;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepDiagramacaoEmail : IPipelineStep
{
    public string Nome => "diagramacao";

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;
    private readonly IReferenciasCliente _referencias;
    private readonly IIconDescricaoCache _iconCache;
    private readonly ILogger<StepDiagramacaoEmail> _logger;

    public StepDiagramacaoEmail(AgenteDefinicao agente, IServicoChat servicoChat, IReferenciasCliente referencias, IIconDescricaoCache iconCache, ILogger<StepDiagramacaoEmail> logger)
    {
        _agente = agente;
        _servicoChat = servicoChat;
        _referencias = referencias;
        _iconCache = iconCache;
        _logger = logger;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.Copy == null)
            return context;

        var prompt = await MontarPromptAsync(context, _referencias, _iconCache, ct);

        var resposta = await _servicoChat.ChamarAgenteAsync(
            context.ChatId,
            _agente.Persona,
            _agente.Modelo,
            prompt,
            "email_diagramacao",
            temperature: _agente.Temperatura,
            maxTokens: _agente.MaxTokens,
            ct: ct);

        if (string.IsNullOrEmpty(resposta))
        {
            _logger.LogWarning("Step diagramacao retornou resposta vazia. Mantendo copy anterior.");
            return context;
        }

        var raw = LimparResposta(resposta);
        var semSaudacao = StripPrimeiraSaudacao(raw);
        var html = StripCtaDuplicado(semSaudacao, context.Copy!.CtaTexto);

        if (IsValidHtml(html))
        {
            context.Copy = new CopyEmailSlots(
                context.Copy.Assunto,
                context.Copy.Preheader,
                context.Copy.Titulo,
                html,
                context.Copy.CtaTexto,
                context.Copy.CtaLink);
        }
        else
        {
            _logger.LogWarning("HTML da diagramacao invalido. Mantendo copy anterior. Resposta: {Resposta}",
                resposta[..Math.Min(200, resposta.Length)]);
        }

        return context;
    }

    internal static async Task<string> MontarPromptAsync(PipelineContext context, IReferenciasCliente referencias, IIconDescricaoCache iconCache, CancellationToken ct = default)
    {
        var copy = context.Copy!;
        var prompt = $"## Copy do email\n";
        prompt += $"Assunto: {copy.Assunto}\n";
        prompt += $"Titulo: {copy.Titulo}\n";
        prompt += $"Corpo original:\n{copy.Corpo}\n";

        if (context.Estrategia?.FaseDados != null)
        {
            prompt += $"\n## Estrategia\n";
            prompt += $"Fase: {context.Estrategia.Fase}\n";
            if (!string.IsNullOrEmpty(context.Estrategia.SubJornada))
                prompt += $"Sub-jornada: {context.Estrategia.SubJornada}\n";
            if (!string.IsNullOrEmpty(context.Estrategia.FaseDados.CorPrincipal))
                prompt += $"Cor principal: {context.Estrategia.FaseDados.CorPrincipal}\n";
            if (context.Estrategia.FaseDados.Temas.Count > 0)
                prompt += $"Temas: {string.Join(", ", context.Estrategia.FaseDados.Temas)}\n";
        }

        if (!string.IsNullOrEmpty(context.Cliente))
        {
            var icons = referencias.ListarAssets(context.Cliente)
                ?.Where(a => a.Tipo == TipoAsset.Icon)
                ?.ToList() ?? new List<AssetVisual>();

            if (icons.Count > 0)
            {
                prompt += $"\n## Icones disponiveis\n";
                prompt += $"Referencie cada icone como assets/{{nome}}.png. Selecione o icone mais adequado pelo significado:\n";
                foreach (var icon in icons)
                {
                    var desc = await iconCache.ObterDescricaoAsync(icon, ct);
                    var ext = Path.GetExtension(icon.Caminho);
                    var nomeComExt = $"{icon.Nome}{ext}";
                    prompt += $"- assets/{nomeComExt} — {desc.ToPromptSection()}\n";
                }
            }
        }

        prompt += $"\n## Instrucao\n";
        prompt += $"Crie secoes HTML diagramadas para o corpo do email. Use os padroes visuais do seu catalogo. Responda APENAS com o HTML das secoes, sem markdown.\n";

        if (context.Refacoes > 0 && !string.IsNullOrEmpty(context.QaFeedback))
        {
            prompt += $"\n## REVISAO NECESSARIA (refacao {context.Refacoes})\n";
            prompt += $"O QA reprovou a versao anterior. Corrija os seguintes problemas:\n";
            prompt += $"{context.QaFeedback}\n";
        }

        return prompt;
    }

    internal static bool IsValidHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        if (DivRegex().IsMatch(html))
            return false;
        if (StyleBlockRegex().IsMatch(html))
            return false;
        if (ScriptRegex().IsMatch(html))
            return false;
        if (!TableRegex().IsMatch(html))
            return false;

        return true;
    }

    private static string LimparResposta(string resposta)
    {
        var html = resposta.Trim();

        if (html.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
            html = html.Substring(7);
        else if (html.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            html = html.Substring(3);

        if (html.EndsWith("```"))
            html = html[..^3];

        return html.Trim();
    }

    internal static string StripPrimeiraSaudacao(string html)
    {
        var comNome = SaudacaoComNomeRegex().Match(html);
        if (comNome.Success)
            return html.Remove(comNome.Index, comNome.Length);

        var semNome = SaudacaoSemNomeRegex().Match(html);
        if (semNome.Success)
        {
            var pMatch = FirstPRegex().Match(html, semNome.Index);
            if (pMatch.Success && pMatch.Index == semNome.Index)
                return html.Remove(pMatch.Index, pMatch.Length);
        }

        return html;
    }

    internal static string StripCtaDuplicado(string html, string ctaTexto)
    {
        if (string.IsNullOrWhiteSpace(ctaTexto))
            return html;
        var alvo = ctaTexto.Trim();
        return AnchorRegex().Replace(html, m =>
        {
            var innerText = TagStripperRegex().Replace(m.Groups[1].Value, "");
            var normalized = string.Join(" ", innerText.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).Trim();
            return normalized.Equals(alvo, StringComparison.OrdinalIgnoreCase) ? string.Empty : m.Value;
        });
    }

    [GeneratedRegex(@"<p\b[^>]*>\s*(?:Ola|Olá|Ol&aacute;|Oi|Prezado|Caro|Querido|Estimado|Bem-vindo)(?!\w).*?%%NOME%%.*?</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex SaudacaoComNomeRegex();

    [GeneratedRegex(@"^\s*<p\b[^>]*>\s*(?:Ola|Olá|Ol&aacute;|Oi|Prezado|Caro|Querido|Estimado|Bem-vindo)(?!\w)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex SaudacaoSemNomeRegex();

    [GeneratedRegex(@"<p\b[^>]*>.*?</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex FirstPRegex();

    [GeneratedRegex(@"<a\b[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex TagStripperRegex();

    [GeneratedRegex(@"<\s*div[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DivRegex();

    [GeneratedRegex(@"<\s*style[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StyleBlockRegex();

    [GeneratedRegex(@"<\s*script[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<\s*table[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TableRegex();
}
