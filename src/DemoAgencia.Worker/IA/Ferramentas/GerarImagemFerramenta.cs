using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class GerarImagemFerramenta : IFerramenta
{
    private readonly ILogger<GerarImagemFerramenta> _logger;
    private readonly IGeradorImagem _openRouter;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;

    public string Nome => "gerar_imagem";
    public string Descricao => "Gera uma imagem a partir de um prompt textual, opcionalmente enriquecido com identidade visual de assets do cliente";

    public GerarImagemFerramenta(ILogger<GerarImagemFerramenta> logger, IGeradorImagem openRouter, IReferenciasCliente referencias, IAnalisadorImagem analisadorImagem)
    {
        _logger = logger;
        _openRouter = openRouter;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
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

        var promptEnriquecido = await EnriquecerComAssetsAsync(prompt, parametros, context, ct);

        _logger.LogInformation("Gerando imagem com prompt: {Prompt}", promptEnriquecido);

        var resultado = await _openRouter.GerarImagemAsync(context.ChatId, promptEnriquecido, ct);

        if (resultado.Sucesso)
        {
            context.Resultado.Imagens.Add(new ImagemGerada(resultado.Bytes!, legenda));
            return "Imagem gerada com sucesso.";
        }

        return $"Falha ao gerar imagem: {resultado.Erro}";
    }

    private async Task<string> EnriquecerComAssetsAsync(string prompt, JsonElement parametros, LoopContext context, CancellationToken ct)
    {
        if (!parametros.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            return prompt;

        var cliente = context.Cliente;
        if (string.IsNullOrEmpty(cliente))
            return prompt;

        var assetsDoCliente = _referencias.ListarAssets(cliente);
        var descricoes = new List<string>();

        foreach (var item in assetsEl.EnumerateArray())
        {
            var id = item.GetString();
            if (string.IsNullOrEmpty(id)) continue;

            var asset = assetsDoCliente.FirstOrDefault(a => a.Id == id);
            if (asset == null) continue;

            try
            {
                var bytes = await File.ReadAllBytesAsync(asset.Caminho, ct);
                var descricao = await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
                if (!string.IsNullOrEmpty(descricao))
                    descricoes.Add($"{asset.Tipo}/{asset.Nome}: {descricao}");
            }
            catch
            {
                if (!string.IsNullOrEmpty(asset.Descricao))
                    descricoes.Add($"{asset.Tipo}/{asset.Nome}: {asset.Descricao}");
            }
        }

        if (descricoes.Count == 0)
            return prompt;

        return prompt + "\n\nVisual identity references:\n" + string.Join("\n", descricoes);
    }
}
