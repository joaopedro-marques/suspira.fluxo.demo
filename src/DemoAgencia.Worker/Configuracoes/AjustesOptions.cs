using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class AjustesOptions
{
    public const string Section = "Ajustes";

    /// <summary>Por quanto tempo a ultima entrega fica disponivel para ajustes.</summary>
    public int TtlHorasEntrega { get; set; } = 24;

    /// <summary>Maximo de rodadas de ajuste por entrega antes de exigir um novo pedido.</summary>
    public int MaxRodadas { get; set; } = 3;

    /// <summary>Refacoes internas do QA dentro de uma rodada de ajuste.</summary>
    public int MaxRefacoesQa { get; set; } = 4;
}
