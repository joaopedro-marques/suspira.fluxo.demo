using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.IA.PreFlight;

[ExcludeFromCodeCoverage]
public class RodadaEsclarecimento
{
    public List<string> Perguntas { get; set; } = new();
    public string? RespostaUsuario { get; set; }
}
