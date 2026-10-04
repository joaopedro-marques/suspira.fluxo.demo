using System.Collections.Concurrent;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class EnriquecedorContextoCliente
{
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly ILogger<EnriquecedorContextoCliente> _logger;
    private readonly ConcurrentDictionary<string, string> _cacheDescricoes = new();

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
        foreach (var caminho in imagens)
        {
            try
            {
                var descricao = _cacheDescricoes.GetOrAdd(caminho, _ =>
                {
                    var bytes = File.ReadAllBytes(caminho);
                    return _analisadorImagem.DescreverImagemAsync(bytes, null, ct).GetAwaiter().GetResult();
                });

                if (!string.IsNullOrEmpty(descricao))
                {
                    var nomeArquivo = Path.GetFileName(caminho);
                    blocos.Add($"## {nomeArquivo}\n{descricao}");
                }
                else
                {
                    _logger.LogWarning("Analise de imagem retornou vazia para {Arquivo}", caminho);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao analisar imagem de referencia {Arquivo}", caminho);
            }
        }

        return string.Join("\n\n", blocos);
    }
}
