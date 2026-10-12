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
            context.Marca = new MarcaEmail(null, null);
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

        context.Marca = new MarcaEmail(logoBytes, logoExt);
        context.LogoSrc = logoBytes != null ? $"assets/logo.{logoExt}" : null;

        if (logoBytes != null)
        {
            context.Resultado.AssetsAnexados.Add(new ImagemGerada(logoBytes, $"logo.{logoExt}"));
        }

        return Task.FromResult(context);
    }
}
