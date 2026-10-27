using System.Collections.Concurrent;
using DemoAgencia.Worker.Configuracoes;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Store em memoria da ultima entrega por chat, mantendo o contexto da pipeline
/// para permitir ajustes pontuais apos a entrega. Entradas expiram por TTL.
/// </summary>
public class EntregaStore
{
    private readonly ConcurrentDictionary<long, (EstadoEntrega Estado, DateTimeOffset CriadoEm)> _entregas = new();
    private readonly AjustesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EntregaStore> _logger;

    public EntregaStore(
        IOptions<AjustesOptions> options,
        TimeProvider timeProvider,
        ILogger<EntregaStore> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Guardar(long chatId, EstadoEntrega estado)
    {
        _entregas[chatId] = (estado, _timeProvider.GetUtcNow());
        _logger.LogInformation(
            "Entrega guardada para chat {ChatId}: versao {Versao}, fase {Fase}, rodadas {Rodadas}",
            chatId, estado.Versao, estado.Fase, estado.RodadasAjuste);
    }

    public EstadoEntrega? Obter(long chatId)
    {
        if (!_entregas.TryGetValue(chatId, out var entrada))
            return null;

        var ttl = TimeSpan.FromHours(_options.TtlHorasEntrega);
        if (_timeProvider.GetUtcNow() - entrada.CriadoEm > ttl)
        {
            _entregas.TryRemove(chatId, out _);
            _logger.LogInformation("Entrega expirada para chat {ChatId}", chatId);
            return null;
        }

        return entrada.Estado;
    }

    public void Remover(long chatId)
    {
        _entregas.TryRemove(chatId, out _);
        _logger.LogInformation("Entrega removida para chat {ChatId}", chatId);
    }
}
