namespace DemoAgencia.Worker.IA;

public class ResultadoPipeline
{
    public string RespostaFinal { get; set; } = string.Empty;
    public byte[]? Imagem { get; set; }
    public string? LegendaImagem { get; set; }
    public List<string> EtapasExecutadas { get; set; } = new();
}
