using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class ListarAssetsFerramenta : IFerramenta
{
    private readonly ILogger<ListarAssetsFerramenta> _logger;
    private readonly IReferenciasCliente _referencias;

    public string Nome => "listar_assets";
    public string Descricao => "Lista os assets visuais (header, footer, icon, logo, foto, post) disponiveis para um cliente";

    public ListarAssetsFerramenta(ILogger<ListarAssetsFerramenta> logger, IReferenciasCliente referencias)
    {
        _logger = logger;
        _referencias = referencias;
    }

    public Task<string> ExecutarAsync(LoopContext context, JsonElement parametros, CancellationToken ct)
    {
        var cliente = parametros.TryGetProperty("cliente", out var clienteEl)
            ? clienteEl.GetString() ?? context.Cliente ?? ""
            : context.Cliente ?? "";

        if (string.IsNullOrWhiteSpace(cliente))
        {
            return Task.FromResult("Falha: cliente nao informado");
        }

        _logger.LogInformation("Listando assets para cliente: {Cliente}", cliente);

        var assets = _referencias.ListarAssets(cliente);
        if (assets.Count == 0)
        {
            return Task.FromResult($"Nenhum asset encontrado para o cliente {cliente}.");
        }

        var linhas = new List<string> { $"Assets disponiveis para {cliente}:" };
        foreach (var asset in assets)
        {
            var desc = string.IsNullOrEmpty(asset.Descricao) ? "(sem descricao)" : asset.Descricao;
            linhas.Add($"- [{asset.Id}] {asset.Tipo}/{asset.Nome}: {desc}");
        }

        return Task.FromResult(string.Join("\n", linhas));
    }
}
