namespace DemoAgencia.Worker.IA;

public interface IHistoricoChat
{
    void AdicionarMensagem(long chatId, string role, string content);
    List<ChatMessage> ObterHistorico(long chatId);
    void LimparHistorico(long chatId);
}
