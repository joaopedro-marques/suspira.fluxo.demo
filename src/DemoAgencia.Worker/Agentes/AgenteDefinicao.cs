namespace DemoAgencia.Worker.Agentes;

public class AgenteDefinicao
{
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string ModeloAlvo { get; set; } = string.Empty;
    public string Papel { get; set; } = "producao";
    public bool Interno { get; set; } = false;
    public double Temperatura { get; set; } = 0.7;
    public List<string> Comandos { get; set; } = new();
    public string Persona { get; set; } = string.Empty;
}
