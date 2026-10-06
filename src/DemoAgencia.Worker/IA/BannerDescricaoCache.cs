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
            try
            {
                var json = await File.ReadAllTextAsync(sidecarPath, ct);
                var cached = JsonSerializer.Deserialize<BannerDescricao>(json, ReadOptions);
                if (cached != null)
                    return cached;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao ler cache de descricao do banner {Path}. Regenerando.", sidecarPath);
            }
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
}
