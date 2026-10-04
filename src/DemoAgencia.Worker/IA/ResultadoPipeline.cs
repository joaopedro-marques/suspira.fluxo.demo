using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.IA;

[ExcludeFromCodeCoverage]
public record ImagemGerada(byte[] Bytes, string? Legenda);

[ExcludeFromCodeCoverage]
public record ResultadoImagem(byte[]? Bytes, string? Erro)
{
    public bool Sucesso => Bytes != null && Bytes.Length > 0;
}

[ExcludeFromCodeCoverage]
public class ResultadoPipeline
{
    public string RespostaFinal { get; set; } = string.Empty;
    public List<ImagemGerada> Imagens { get; set; } = new();
    public List<ImagemGerada> AssetsAnexados { get; set; } = new();
    public List<string> EtapasExecutadas { get; set; } = new();
}
