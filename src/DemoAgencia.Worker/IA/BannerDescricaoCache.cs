using System.Collections.Concurrent;
using System.Text.Json;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA;

public interface IBannerDescricaoCache
{
    Task<BannerDescricao> ObterDescricaoAsync(AssetVisual banner, CancellationToken ct = default);
}

public class BannerDescricaoCache : IBannerDescricaoCache
{
    private readonly IAnalisadorImagem _analisador;
    private readonly ILogger<BannerDescricaoCache> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public BannerDescricaoCache(IAnalisadorImagem analisador, ILogger<BannerDescricaoCache> logger)
    {
        _analisador = analisador;
        _logger = logger;
    }

    public async Task<BannerDescricao> ObterDescricaoAsync(AssetVisual banner, CancellationToken ct = default)
    {
        var sidecarPath = banner.Caminho + ".desc.json";

        if (File.Exists(sidecarPath))
        {
            var cached = LerSidecar(sidecarPath);
            if (cached != null)
                return cached;
        }

        var sem = _locks.GetOrAdd(sidecarPath, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (File.Exists(sidecarPath))
            {
                var cached = LerSidecar(sidecarPath);
                if (cached != null)
                    return cached;
            }

            var bytes = await File.ReadAllBytesAsync(banner.Caminho, ct);
            var descricao = await _analisador.DescreverBannerAsync(bytes, null, ct);

            try
            {
                var json = JsonSerializer.Serialize(descricao, SnakeOptions);
                await File.WriteAllTextAsync(sidecarPath, json, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gravar cache de descricao do banner {Path}", sidecarPath);
            }

            return descricao;
        }
        finally
        {
            sem.Release();
        }
    }

    private BannerDescricao? LerSidecar(string sidecarPath)
    {
        try
        {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize<BannerDescricao>(json, ReadOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler cache de descricao do banner {Path}. Regenerando.", sidecarPath);
            return null;
        }
    }
}
