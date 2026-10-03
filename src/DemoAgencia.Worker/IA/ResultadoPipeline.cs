namespace DemoAgencia.Worker.IA;

public record ImagemGerada(byte[] Bytes, string? Legenda);

public class ResultadoPipeline
{
    public string RespostaFinal { get; set; } = string.Empty;
    public List<ImagemGerada> Imagens { get; set; } = new();
    public List<string> EtapasExecutadas { get; set; } = new();
}
