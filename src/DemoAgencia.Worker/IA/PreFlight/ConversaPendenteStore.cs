using System.Collections.Concurrent;
using DemoAgencia.Worker.Configuracoes;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.PreFlight;

public class ConversaPendenteStore
{
    private readonly ConcurrentDictionary<long, (EstadoPreFlight Estado, DateTimeOffset CriadoEm)> _pendencias = new();
    private readonly PreFlightOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConversaPendenteStore> _logger;

    public ConversaPendenteStore(
        IOptions<PreFlightOptions> options,
        TimeProvider timeProvider,
        ILogger<ConversaPendenteStore> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Guardar(long chatId, EstadoPreFlight estado)
    {
        _pendencias[chatId] = (estado, _timeProvider.GetUtcNow());
        _logger.LogInformation("Pendencia guardada para chat {ChatId}", chatId);
    }

    public EstadoPreFlight? Obter(long chatId)
    {
        if (!_pendencias.TryGetValue(chatId, out var entrada))
            return null;

        var timeout = TimeSpan.FromMinutes(_options.TimeoutMinutosPendencia);
        if (_timeProvider.GetUtcNow() - entrada.CriadoEm > timeout)
        {
            _pendencias.TryRemove(chatId, out _);
            _logger.LogInformation("Pendencia expirada para chat {ChatId}", chatId);
            return null;
        }

        return entrada.Estado;
    }

    public void Remover(long chatId)
    {
        _pendencias.TryRemove(chatId, out _);
        _logger.LogInformation("Pendencia removida para chat {ChatId}", chatId);
    }
}
