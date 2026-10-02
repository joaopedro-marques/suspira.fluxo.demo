namespace DemoAgencia.Worker.Configuracoes;

public class LoopOptions
{
    public const string Section = "Loop";
    public int MaxTurnos { get; set; } = 8;
    public int MaxRefacoesQa { get; set; } = 2;
    public string MensagemFalha { get; set; } = "Falha no loop";
    public string MensagemForaContexto { get; set; } = "Fora do contexto";
}
