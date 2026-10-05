using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class LoopOptions
{
    public const string Section = "Loop";
    public int MaxTurnos { get; set; } = 24;
    public int MaxRefacoesQa { get; set; } = 2;
    public int MaxRetriesGratis { get; set; } = 4;
    public int MaxImagensPorDeck { get; set; } = 10;
    public int MaxTokensOrquestrador { get; set; } = 4000;
    public int MaxCharsResultado { get; set; } = 2000;
    public int MaxCharsContexto { get; set; } = 16000;
    public int MaxRetriesTransientes { get; set; } = 2;
    public int DelayTransienteSegundos { get; set; } = 15;
    public string MensagemFalha { get; set; } = "Falha no loop";
    public string MensagemForaContexto { get; set; } = "Fora do contexto";
}
