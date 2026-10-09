using System.Collections.Concurrent;
using DemoAgencia.Worker.Configuracoes;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.IA.Onboarding;

public class OnboardingStore
{
    private readonly ConcurrentDictionary<long, (OnboardingState Estado, DateTimeOffset CriadoEm)> _pendencias = new();
    private readonly PreFlightOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OnboardingStore> _logger;

    public OnboardingStore(
        IOptions<PreFlightOptions> options,
        TimeProvider timeProvider,
        ILogger<OnboardingStore> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Guardar(long chatId, OnboardingState estado)
    {
        _pendencias[chatId] = (estado, _timeProvider.GetUtcNow());
        _logger.LogInformation("Onboarding guardado para chat {ChatId} passo {Passo}", chatId, estado.PassoAtual);
    }

    public OnboardingState? Obter(long chatId)
    {
        if (!_pendencias.TryGetValue(chatId, out var entrada))
            return null;

        var timeout = TimeSpan.FromMinutes(_options.TimeoutMinutosPendencia);
        if (_timeProvider.GetUtcNow() - entrada.CriadoEm > timeout)
        {
            _pendencias.TryRemove(chatId, out _);
            _logger.LogInformation("Onboarding expirado para chat {ChatId}", chatId);
            return null;
        }

        return entrada.Estado;
    }

    public void Remover(long chatId)
    {
        _pendencias.TryRemove(chatId, out _);
        _logger.LogInformation("Onboarding removido para chat {ChatId}", chatId);
    }
}
