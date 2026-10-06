using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepAssetsEmail : IPipelineStep
{
    public string Nome => "assets";

    private readonly IReferenciasCliente _referencias;

    public StepAssetsEmail(IReferenciasCliente referencias)
    {
        _referencias = referencias;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var html = context.Html;
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(context.Cliente))
            return Task.FromResult(context);

        var matches = AssetsSrcRegex().Matches(html);
        var assets = _referencias.ListarAssets(context.Cliente);

        foreach (Match match in matches)
        {
            var referencedName = match.Groups[1].Value;
            if (string.IsNullOrEmpty(referencedName))
                continue;

            if (context.Resultado.AssetsAnexados.Any(a => a.Legenda == referencedName))
                continue;

            var asset = assets.FirstOrDefault(a =>
                Path.GetFileName(a.Caminho).EndsWith(referencedName, StringComparison.OrdinalIgnoreCase));

            if (asset == null || !File.Exists(asset.Caminho))
                continue;

            var bytes = File.ReadAllBytes(asset.Caminho);
            context.Resultado.AssetsAnexados.Add(new ImagemGerada(bytes, referencedName));
        }

        return Task.FromResult(context);
    }

    [GeneratedRegex(@"src=""assets/([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AssetsSrcRegex();
}
