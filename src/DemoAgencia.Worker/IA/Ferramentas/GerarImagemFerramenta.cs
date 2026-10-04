using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class GerarImagemFerramenta : IFerramenta
{
    private readonly ILogger<GerarImagemFerramenta> _logger;
    private readonly IGeradorImagem _openRouter;

    public string Nome => "gerar_imagem";
    public string Descricao => "Gera uma imagem a partir de um prompt textual";

    public GerarImagemFerramenta(ILogger<GerarImagemFerramenta> logger, IGeradorImagem openRouter)
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

        var legenda = parametros.TryGetProperty("legenda", out var legEl)
            ? legEl.GetString()
            : null;

        _logger.LogInformation("Gerando imagem com prompt: {Prompt}", prompt);

        var resultado = await _openRouter.GerarImagemAsync(context.ChatId, prompt, ct);

        if (resultado.Sucesso)
        {
            context.Resultado.Imagens.Add(new ImagemGerada(resultado.Bytes!, legenda));
            return "Imagem gerada com sucesso.";
        }

        return $"Falha ao gerar imagem: {resultado.Erro}";
    }
}
