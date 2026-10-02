namespace DemoAgencia.Worker.IA;

public class HistoricoChat : IHistoricoChat
{
    private readonly Dictionary<long, List<ChatMessage>> _historicos = new();
    private readonly Dictionary<long, DateTime> _ultimaAtividade = new();
    private readonly object _lock = new();
    private const int MaxMensagensPorChat = 20;
    private const int TimeoutMinutos = 60;
    private DateTime _ultimaLimpeza = DateTime.UtcNow;
    private static readonly TimeSpan IntervaloLimpeza = TimeSpan.FromMinutes(5);

    public void AdicionarMensagem(long chatId, string role, string content)
    {
        lock (_lock)
        {
            if (!_historicos.ContainsKey(chatId))
            {
                _historicos[chatId] = new List<ChatMessage>();
            }

            _historicos[chatId].Add(new ChatMessage(role, content));
            _ultimaAtividade[chatId] = DateTime.UtcNow;

            if (_historicos[chatId].Count > MaxMensagensPorChat)
            {
                _historicos[chatId].RemoveRange(0, _historicos[chatId].Count - MaxMensagensPorChat);
            }

            LimparChatsInativos();
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
            _ultimaAtividade.Remove(chatId);
        }
    }

    private void LimparChatsInativos()
    {
        if (DateTime.UtcNow - _ultimaLimpeza < IntervaloLimpeza)
            return;

        var cutoff = DateTime.UtcNow.AddMinutes(-TimeoutMinutos);
        var chatsRemover = _ultimaAtividade
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var chatId in chatsRemover)
        {
            _historicos.Remove(chatId);
            _ultimaAtividade.Remove(chatId);
        }

        _ultimaLimpeza = DateTime.UtcNow;
    }
}
