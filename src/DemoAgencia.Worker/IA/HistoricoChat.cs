namespace DemoAgencia.Worker.IA;

public class HistoricoChat : IHistoricoChat
{
    private readonly Dictionary<long, List<ChatMessage>> _historicos = new();
    private readonly object _lock = new();
    private const int MaxMensagensPorChat = 20;

    public void AdicionarMensagem(long chatId, string role, string content)
    {
        lock (_lock)
        {
            if (!_historicos.ContainsKey(chatId))
            {
                _historicos[chatId] = new List<ChatMessage>();
            }

            _historicos[chatId].Add(new ChatMessage(role, content));

            if (_historicos[chatId].Count > MaxMensagensPorChat)
            {
                _historicos[chatId].RemoveRange(0, _historicos[chatId].Count - MaxMensagensPorChat);
            }
        }
    }

    public List<ChatMessage> ObterHistorico(long chatId)
    {
        lock (_lock)
        {
            return _historicos.TryGetValue(chatId, out var hist) 
                ? new List<ChatMessage>(hist) 
                : new List<ChatMessage>();
        }
    }

    public void LimparHistorico(long chatId)
    {
        lock (_lock)
        {
            _historicos.Remove(chatId);
        }
    }
}
