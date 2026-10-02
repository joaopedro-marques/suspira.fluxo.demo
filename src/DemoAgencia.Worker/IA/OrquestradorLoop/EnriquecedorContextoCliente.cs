using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class EnriquecedorContextoCliente
{
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;

    public EnriquecedorContextoCliente(IReferenciasCliente referencias, IAnalisadorImagem analisadorImagem)
    {
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
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
                var bytes = await File.ReadAllBytesAsync(caminho, ct);
                var descricao = await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
                if (!string.IsNullOrEmpty(descricao))
                {
                    var nomeArquivo = Path.GetFileName(caminho);
                    blocos.Add($"## {nomeArquivo}\n{descricao}");
                }
            }
            catch
            {
            }
        }

        return string.Join("\n\n", blocos);
    }
}
