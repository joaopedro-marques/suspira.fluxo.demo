using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.IA.PreFlight;

[ExcludeFromCodeCoverage]
public class EstadoPreFlight
{
    public long ChatId { get; set; }
    public string MensagemOriginal { get; set; } = string.Empty;
    public string? Cliente { get; set; }
    public int RodadasPerguntas { get; set; }
    public List<RodadaEsclarecimento> Rodadas { get; set; } = new();
}
