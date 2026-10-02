namespace DemoAgencia.Worker.IA.Ferramentas;

public class FerramentaRegistry
{
    private readonly Dictionary<string, IFerramenta> _ferramentas = new();

    public void Registrar(IFerramenta ferramenta)
    {
        _ferramentas[ferramenta.Nome] = ferramenta;
    }

    public IFerramenta? Obter(string nome)
    {
        return _ferramentas.TryGetValue(nome, out var ferramenta) ? ferramenta : null;
    }

    public IReadOnlyCollection<IFerramenta> Listar()
    {
        return _ferramentas.Values;
    }
}
