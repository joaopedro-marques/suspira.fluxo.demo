using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepMapaEmocionalEmail : IPipelineStep
{
    public string Nome => "mapa_emocional";

    private readonly AgenteDefinicao _agente;
    private readonly IServicoChat _servicoChat;
    private readonly ILogger<StepMapaEmocionalEmail> _logger;

    public StepMapaEmocionalEmail(AgenteDefinicao agente, IServicoChat servicoChat, ILogger<StepMapaEmocionalEmail> logger)
    {
        _agente = agente;
        _servicoChat = servicoChat;
        _logger = logger;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.Estrategia == null || context.Estrategia.MapaEmocional.Count == 0)
            return context;

        var prompt = MontarPrompt(context);

        string resposta;
        try
        {
            resposta = await _servicoChat.ChamarAgenteAsync(
                context.ChatId,
                _agente.Persona,
                _agente.Modelo,
                prompt,
                "email_mapa_emocional",
                temperature: _agente.Temperatura,
                maxTokens: _agente.MaxTokens,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StepMapaEmocionalEmail: falha ao chamar LLM. Seguindo sem associacao E/R.");
            return context;
        }

        var etapa = ParseEtapa(resposta);
        if (string.IsNullOrEmpty(etapa))
        {
            _logger.LogWarning("StepMapaEmocionalEmail: resposta sem etapa parseavel. Resposta: {Resposta}",
                resposta?[..Math.Min(200, resposta?.Length ?? 0)]);
            return context;
        }

        var etapaMapa = context.Estrategia.MapaEmocional
            .FirstOrDefault(e => e.Etapa.Equals(etapa, StringComparison.OrdinalIgnoreCase));

        if (etapaMapa == null)
        {
            _logger.LogWarning("StepMapaEmocionalEmail: etapa '{Etapa}' nao encontrada no mapa emocional. Seguindo sem associacao.", etapa);
            return context;
        }

        context.Estrategia.EtapaMapaEmocional = etapaMapa.Etapa;
        context.Estrategia.FatorDecisao = etapaMapa.FatorDecisao;

        return context;
    }

    internal static string MontarPrompt(PipelineContext context)
    {
        var estrategia = context.Estrategia!;
        var brief = context.Brief;

        var prompt = "Voce e um classificador de etapa de jornada emocional.\n";
        prompt += "Dado o contexto do email abaixo, escolha EXATAMENTE UMA etapa da lista que melhor se aplica ao assunto.\n\n";

        prompt += "## Etapas disponiveis (escolha uma)\n";
        foreach (var etapa in estrategia.MapaEmocional)
        {
            prompt += $"- {etapa.Etapa} (fator: {etapa.FatorDecisao})";
            if (etapa.Sentimentos.Count > 0)
                prompt += $" — sentimentos: {string.Join(", ", etapa.Sentimentos)}";
            prompt += "\n";
        }

        prompt += "\n## Contexto do email\n";
        if (!string.IsNullOrEmpty(estrategia.Fase))
            prompt += $"Fase da jornada: {estrategia.Fase}\n";
        if (!string.IsNullOrEmpty(estrategia.SubJornada))
            prompt += $"Sub-jornada: {estrategia.SubJornada}\n";
        if (!string.IsNullOrEmpty(estrategia.TemaSelecionado))
            prompt += $"Tema: {estrategia.TemaSelecionado}\n";
        else if (estrategia.FaseDados?.Temas.Count > 0)
            prompt += $"Temas da fase: {string.Join(", ", estrategia.FaseDados.Temas)}\n";
        if (!string.IsNullOrEmpty(brief.Objetivo))
            prompt += $"Objetivo: {brief.Objetivo}\n";
        if (!string.IsNullOrEmpty(brief.Oferta))
            prompt += $"Oferta/produto: {brief.Oferta}\n";
        if (!string.IsNullOrEmpty(context.MensagemOriginal))
            prompt += $"Mensagem do usuario: {context.MensagemOriginal}\n";

        prompt += "\n## Saida (JSON exato, sem fences, sem explicacao)\n";
        prompt += "{\"etapa\": \"NOME_EXATO_DA_ETAPA\"}\n";
        prompt += "Use apenas os nomes exatos listados acima. Nao invente etapas.\n";

        return prompt;
    }

    private static string? ParseEtapa(string resposta)
    {
        var json = JsonHelper.ExtrairJson(resposta);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.TryGetProperty("etapa", out var e) ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
