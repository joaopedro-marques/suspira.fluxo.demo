using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public record ResultadoQa(bool Aprovado, string? Feedback);

public class GateQualidade
{
    private readonly IServicoChat _servicoChat;
    private readonly IAgentesCatalogo _agentesCatalogo;

    public GateQualidade(IServicoChat servicoChat, IAgentesCatalogo agentesCatalogo)
    {
        _servicoChat = servicoChat;
        _agentesCatalogo = agentesCatalogo;
    }

    public async Task<ResultadoQa> AvaliarAsync(
        long chatId,
        string briefing,
        string entregavel,
        CancellationToken ct = default)
    {
        var qualidade = _agentesCatalogo.ObterPorPapel("qualidade");
        if (qualidade == null)
            return new ResultadoQa(true, null);

        var qaPrompt = $"Briefing original: {briefing}\n\nEntregavel:\n{entregavel}";
        var qaResult = await _servicoChat.ChamarAgenteAsync(
            chatId,
            qualidade.Persona,
            qualidade.ModeloAlvo,
            qaPrompt,
            "loop_qualidade",
            temperature: qualidade.Temperatura,
            ct: ct);

        var qaJson = ParserDecisao.ExtrairJson(qaResult);
        if (string.IsNullOrEmpty(qaJson))
            return new ResultadoQa(true, null);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(qaJson);
            var aprovado = doc.RootElement.TryGetProperty("aprovado", out var apEl) && apEl.GetBoolean();
            var feedback = doc.RootElement.TryGetProperty("feedback", out var fbEl) ? fbEl.GetString() : null;
            return new ResultadoQa(aprovado, feedback);
        }
        catch
        {
            return new ResultadoQa(true, null);
        }
    }
}
