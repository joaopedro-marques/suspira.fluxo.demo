namespace DemoAgencia.Worker.Seguranca;

public class RateLimiterService
{
    private readonly IConfiguration _configuration;
    private readonly Dictionary<long, Queue<DateTime>> _janelas = new();
    private readonly object _lock = new();

    public RateLimiterService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool PodeProcessar(long chatId)
    {
        var maxPorMinuto = _configuration.GetValue<int>("Seguranca:MaxMensagensPorMinuto", 5);
        var janela = TimeSpan.FromMinutes(1);

        lock (_lock)
        {
            if (!_janelas.ContainsKey(chatId))
            {
                _janelas[chatId] = new Queue<DateTime>();
            }

            var fila = _janelas[chatId];
            var agora = DateTime.UtcNow;

            while (fila.Count > 0 && agora - fila.Peek() > janela)
            {
                fila.Dequeue();
            }

            if (fila.Count >= maxPorMinuto)
            {
                return false;
            }

            fila.Enqueue(agora);
            return true;
        }
    }
}
