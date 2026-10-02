using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class GerarImagemFerramenta : IFerramenta
{
    private readonly ILogger<GerarImagemFerramenta> _logger;
    private readonly OpenRouterService _openRouter;

    public string Nome => "gerar_imagem";
    public string Descricao => "Gera uma imagem a partir de um prompt textual";

    public GerarImagemFerramenta(ILogger<GerarImagemFerramenta> logger, OpenRouterService openRouter)
    {
        _logger = logger;
        _openRouter = openRouter;
    }

    public async Task<string> ExecutarAsync(LoopContext context, JsonElement parametros, CancellationToken ct)
    {
        var prompt = parametros.TryGetProperty("prompt", out var promptEl)
            ? promptEl.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Falha: prompt vazio";
        }

        _logger.LogInformation("Gerando imagem com prompt: {Prompt}", prompt);

        var imagemBytes = await _openRouter.GerarImagemAsync(context.ChatId, prompt, ct);

        if (imagemBytes != null)
        {
            context.Resultado.Imagem = imagemBytes;
            context.Resultado.LegendaImagem = prompt;
            return $"Imagem gerada com sucesso. Prompt: {prompt}";
        }

        return "Falha ao gerar imagem";
    }
}
