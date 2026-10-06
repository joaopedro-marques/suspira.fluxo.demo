namespace DemoAgencia.Worker.Referencias;

public interface IReferenciasCliente
{
    string ObterReferenciasTexto(string cliente);
    IReadOnlyCollection<AssetVisual> ListarAssets(string cliente);
    IReadOnlyCollection<string> ListarClientes();
    EstrategiaCliente? ObterEstrategia(string cliente);
    AssetVisual? SelecionarBanner(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto);
    IReadOnlyList<AssetVisual> SelecionarBannersRanked(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto, int max);
}
