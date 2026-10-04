using System.Collections.Concurrent;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class EnriquecedorContextoCliente
{
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly ILogger<EnriquecedorContextoCliente> _logger;
    private readonly ConcurrentDictionary<string, Task<string>> _cacheDescricoes = new();

    public EnriquecedorContextoCliente(IReferenciasCliente referencias, IAnalisadorImagem analisadorImagem, ILogger<EnriquecedorContextoCliente> logger)
    {
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _logger = logger;
    }

    public async Task<string> ObterContextoAsync(string cliente, CancellationToken ct = default)
    {
        var blocos = new List<string>();

        var texto = _referencias.ObterReferenciasTexto(cliente);
        if (!string.IsNullOrEmpty(texto))
        {
            blocos.Add(texto);
        }

        var imagens = _referencias.ListarImagens(cliente) ?? Array.Empty<string>();
        var indiceCatalogo = blocos.Count;

        foreach (var caminho in imagens)
        {
            var task = _cacheDescricoes.GetOrAdd(caminho, k => CarregarDescricaoAsync(k, ct));
            try
            {
                var descricao = await task;

                if (!string.IsNullOrEmpty(descricao))
                {
                    var nomeArquivo = Path.GetFileName(caminho);
                    blocos.Add($"- {nomeArquivo}: {descricao}");
                }
                else
                {
                    _logger.LogWarning("Analise de imagem retornou vazia para {Arquivo}", caminho);
                }
            }
            catch (Exception ex)
            {
                _cacheDescricoes.TryRemove(new(caminho, task));
                _logger.LogWarning(ex, "Falha ao analisar imagem de referencia {Arquivo}", caminho);
            }
        }

        if (blocos.Count > indiceCatalogo)
        {
            blocos.Insert(indiceCatalogo, "## Catalogo de assets visuais");
        }

        return string.Join("\n", blocos);
    }

    private async Task<string> CarregarDescricaoAsync(string caminho, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(caminho, ct);
        return await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
    }
}
