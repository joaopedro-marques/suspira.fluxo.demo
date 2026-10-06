using System.Text.Json;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA;

public interface IIconDescricaoCache
{
    Task<IconDescricao> ObterDescricaoAsync(AssetVisual icon, CancellationToken ct = default);
}

public class IconDescricaoCache : IIconDescricaoCache
{
    private readonly IAnalisadorImagem _analisador;

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

    public IconDescricaoCache(IAnalisadorImagem analisador)
    {
        _analisador = analisador;
    }

    public async Task<IconDescricao> ObterDescricaoAsync(AssetVisual icon, CancellationToken ct = default)
    {
        var sidecarPath = icon.Caminho + ".desc.json";

        if (File.Exists(sidecarPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(sidecarPath, ct);
                var cached = JsonSerializer.Deserialize<IconDescricao>(json, ReadOptions);
                if (cached != null)
                    return cached;
            }
            catch
            {
            }
        }

        var bytes = await File.ReadAllBytesAsync(icon.Caminho, ct);
        var descricao = await _analisador.DescreverIconeAsync(bytes, null, ct);

        try
        {
            var json = JsonSerializer.Serialize(descricao, SnakeOptions);
            await File.WriteAllTextAsync(sidecarPath, json, ct);
        }
        catch
        {
        }

        return descricao;
    }
}
