using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class AnexarAssetFerramenta : IFerramenta
{
    private readonly ILogger<AnexarAssetFerramenta> _logger;
    private readonly IReferenciasCliente _referencias;

    public string Nome => "anexar_asset";
    public string Descricao => "Anexa um asset visual pre-existente (header, footer, icon, logo, foto, post) ao resultado final para envio ao usuario";

    public AnexarAssetFerramenta(ILogger<AnexarAssetFerramenta> logger, IReferenciasCliente referencias)
    {
        _logger = logger;
        _referencias = referencias;
    }

    public async Task<string> ExecutarAsync(LoopContext context, JsonElement parametros, CancellationToken ct)
    {
        var assetId = parametros.TryGetProperty("asset_id", out var idEl)
            ? idEl.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(assetId))
        {
            return "Falha: asset_id nao informado";
        }

        var cliente = context.Cliente;
        if (string.IsNullOrEmpty(cliente))
        {
            return "Falha: cliente nao definido no contexto";
        }

        var assets = _referencias.ListarAssets(cliente);
        var asset = assets.FirstOrDefault(a => a.Id == assetId);
        if (asset == null)
        {
            return $"Falha: asset {assetId} nao encontrado para o cliente {cliente}";
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(asset.Caminho, ct);
            var legenda = $"{asset.Tipo}/{asset.Nome}";
            context.Resultado.AssetsAnexados.Add(new ImagemGerada(bytes, legenda));
            _logger.LogInformation("Asset anexado: {AssetId} ({Tipo}/{Nome})", assetId, asset.Tipo, asset.Nome);
            return $"Asset {assetId} ({asset.Tipo}/{asset.Nome}) anexado com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler asset {AssetId}", assetId);
            return $"Falha ao ler asset {assetId}: {ex.Message}";
        }
    }
}
