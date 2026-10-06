using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class PreFlightOptions
{
    public const string Section = "PreFlight";
    public int TimeoutMinutosPendencia { get; set; } = 15;
    public int MaxRodadasPerguntas { get; set; } = 2;
}
