namespace DemoAgencia.Worker.Configuracoes;

public class LoopOptions
{
    public const string Section = "Loop";
    public int MaxTurnos { get; set; } = 8;
    public int MaxRefacoesQa { get; set; } = 2;
    public int MaxTokensOrquestrador { get; set; } = 4000;
    public int MaxCharsResultado { get; set; } = 2000;
    public int MaxCharsContexto { get; set; } = 16000;
    public string MensagemFalha { get; set; } = "Falha no loop";
    public string MensagemForaContexto { get; set; } = "Fora do contexto";
}
