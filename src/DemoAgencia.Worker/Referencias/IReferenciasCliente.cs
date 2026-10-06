namespace DemoAgencia.Worker.Referencias;

public interface IReferenciasCliente
{
    string ObterReferenciasTexto(string cliente);
    IReadOnlyCollection<AssetVisual> ListarAssets(string cliente);
    IReadOnlyCollection<string> ListarClientes();
    EstrategiaCliente? ObterEstrategia(string cliente);
}
