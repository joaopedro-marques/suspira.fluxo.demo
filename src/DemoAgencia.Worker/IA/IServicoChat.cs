namespace DemoAgencia.Worker.IA;

public interface IServicoChat
{
    Task<string> ChamarAgenteAsync(
        long chatId,
        string persona,
        string modelo,
        string instrucoes,
        string etapaNome = "pipeline-step",
        double temperature = 0.7,
        int maxTokens = 2000,
        CancellationToken ct = default);
}
