using System.Collections.Concurrent;
using System.Threading.Channels;
using Telegram.Bot.Types;

namespace DemoAgencia.Worker.Telegram;

public class UpdateDispatcher
{
    private readonly ConcurrentDictionary<long, Channel<Update>> _canais = new();
    private readonly ConcurrentDictionary<long, Task> _consumers = new();
    private readonly Func<Update, CancellationToken, Task> _handler;
    private readonly int _capacidade;
    private readonly ILogger<UpdateDispatcher> _logger;
    private readonly object _lock = new();

    public UpdateDispatcher(
        Func<Update, CancellationToken, Task> handler,
        int capacidade,
        ILogger<UpdateDispatcher> logger)
    {
        _handler = handler;
        _capacidade = capacidade;
        _logger = logger;
    }

    public ValueTask<bool> EnfileirarAsync(Update update, CancellationToken ct = default)
    {
        var chatId = update.Message?.Chat.Id ?? 0;
        var canal = ObterOuCriarCanal(chatId);
        var aceito = canal.Writer.TryWrite(update);
        if (!aceito)
        {
            _logger.LogWarning("Fila do chat {ChatId} cheia ({Capacidade}), descartando update {UpdateId}",
                chatId, _capacidade, update.Id);
        }
        return new ValueTask<bool>(aceito);
    }

    public bool TemFilaPendente(long chatId)
    {
        if (_canais.TryGetValue(chatId, out var canal))
            return canal.Reader.Count > 0;
        return false;
    }

    public void Liberar()
    {
        lock (_lock)
        {
            foreach (var canal in _canais.Values)
                canal.Writer.TryComplete();
        }
    }

    public async Task DrainAsync(TimeSpan timeout)
    {
        Liberar();

        var consumers = _consumers.Values.ToArray();
        if (consumers.Length == 0) return;

        var allCompleted = Task.WhenAll(consumers);
        var completed = await Task.WhenAny(allCompleted, Task.Delay(timeout));

        if (completed != allCompleted)
            _logger.LogWarning("Drain timeout ({Timeout}s); {Count} consumers ainda ativos",
                timeout.TotalSeconds, consumers.Length);
    }

    private Channel<Update> ObterOuCriarCanal(long chatId)
    {
        if (_canais.TryGetValue(chatId, out var existente))
            return existente;

        lock (_lock)
        {
            if (_canais.TryGetValue(chatId, out existente))
                return existente;

            var canal = Channel.CreateBounded<Update>(new BoundedChannelOptions(_capacidade)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite,
            });

            var consumerTask = Task.Run(() => ConsumirAsync(chatId, canal.Reader));
            _canais[chatId] = canal;
            _consumers[chatId] = consumerTask;
            return canal;
        }
    }

    private async Task ConsumirAsync(long chatId, ChannelReader<Update> reader)
    {
        try
        {
            await foreach (var update in reader.ReadAllAsync())
            {
                try
                {
                    await _handler(update, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro processando update {UpdateId} do chat {ChatId}",
                        update.Id, chatId);
                }
            }
        }
        finally
        {
            _canais.TryRemove(chatId, out _);
            _consumers.TryRemove(chatId, out _);
        }
    }
}
