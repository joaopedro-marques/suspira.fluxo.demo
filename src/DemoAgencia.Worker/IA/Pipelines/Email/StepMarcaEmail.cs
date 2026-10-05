using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepMarcaEmail : IPipelineStep
{
    public string Nome => "marca";

    private readonly IReferenciasCliente _referencias;

    public StepMarcaEmail(IReferenciasCliente referencias)
    {
        _referencias = referencias;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var cliente = context.Cliente;
        if (string.IsNullOrEmpty(cliente))
        {
            context.Marca = new MarcaEmail(null, null, null, null);
            context.LogoSrc = null;
            return Task.FromResult(context);
        }

        var assets = _referencias.ListarAssets(cliente);
        var logo = assets.FirstOrDefault(a => a.Tipo == TipoAsset.Logo);

        byte[]? logoBytes = null;
        string? logoExt = null;
        if (logo != null && File.Exists(logo.Caminho))
        {
            logoBytes = File.ReadAllBytes(logo.Caminho);
            logoExt = Path.GetExtension(logo.Caminho).TrimStart('.');
        }

        var textoRefs = _referencias.ObterReferenciasTexto(cliente);
        var (cores, tom) = ExtrairMetadata(textoRefs);

        context.Marca = new MarcaEmail(tom, cores, logoBytes, logoExt);
        context.LogoSrc = logoBytes != null ? $"assets/logo.{logoExt}" : null;

        if (logoBytes != null)
        {
            context.Resultado.AssetsAnexados.Add(new ImagemGerada(logoBytes, $"logo.{logoExt}"));
        }

        return Task.FromResult(context);
    }

    private static (string? cores, string? tom) ExtrairMetadata(string textoRefs)
    {
        if (string.IsNullOrEmpty(textoRefs))
            return (null, null);

        string? cores = null;
        string? tom = null;

        var linhas = textoRefs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var linha in linhas)
        {
            var linhaLower = linha.ToLowerInvariant();
            if (linhaLower.Contains("cor") || linhaLower.Contains("paleta") || linhaLower.Contains("color"))
            {
                cores = linha.Trim();
            }
            else if (linhaLower.Contains("tom") || linhaLower.Contains("voz") || linhaLower.Contains("tone"))
            {
                tom = linha.Trim();
            }
        }

        return (cores, tom);
    }
}
