using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class ConcorrenciaOptions
{
    public const string Section = "Concorrencia";
    public int MaxPipelinesSimultaneos { get; set; } = 3;
    public int CapacidadeFilaPorChat { get; set; } = 64;
    public int TimeoutDrainSegundos { get; set; } = 30;
}
