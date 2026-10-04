using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class SegurancaOptions
{
    public const string Section = "Seguranca";
    public bool AnonimizarDados { get; set; } = true;
    public int MaxMensagensPorMinuto { get; set; } = 5;
}
