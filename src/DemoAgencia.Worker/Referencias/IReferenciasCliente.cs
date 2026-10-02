namespace DemoAgencia.Worker.Referencias;

public interface IReferenciasCliente
{
    string ObterReferenciasTexto(string cliente);
    IReadOnlyCollection<string> ListarImagens(string cliente);
}
