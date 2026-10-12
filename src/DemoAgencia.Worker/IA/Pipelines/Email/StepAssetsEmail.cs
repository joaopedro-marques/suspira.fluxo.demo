using System.Text.RegularExpressions;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public partial class StepAssetsEmail : IPipelineStep
{
    public string Nome => "assets";

    private readonly IReferenciasCliente _referencias;
    private readonly ILogger<StepAssetsEmail> _logger;

    public StepAssetsEmail(IReferenciasCliente referencias, ILogger<StepAssetsEmail> logger)
    {
        _referencias = referencias;
        _logger = logger;
    }

    public virtual Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var html = context.Html;
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(context.Cliente))
            return Task.FromResult(context);

        var matches = AssetsSrcRegex().Matches(html);
        var assets = _referencias.ListarAssets(context.Cliente);
        var tagsParaRemover = new List<string>();

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
            {
                _logger.LogWarning("Asset referenciado nao encontrado: {Asset}. Removendo tag do HTML.", referencedName);
                
                var startPos = Math.Max(0, match.Index - 200);
                var searchLength = Math.Min(html.Length - startPos, match.Index + match.Length + 200 - startPos);
                var searchRegion = html.Substring(startPos, searchLength);
                var fullImgTagMatch = FullImgTagRegex().Match(searchRegion);
                while (fullImgTagMatch.Success)
                {
                    if (fullImgTagMatch.Value.Contains(match.Value))
                    {
                        tagsParaRemover.Add(fullImgTagMatch.Value);
                        break;
                    }
                    fullImgTagMatch = fullImgTagMatch.NextMatch();
                }
                continue;
            }

            var bytes = File.ReadAllBytes(asset.Caminho);
            context.Resultado.AssetsAnexados.Add(new ImagemGerada(bytes, referencedName));
        }

        foreach (var tag in tagsParaRemover.Distinct())
        {
            html = html.Replace(tag, "");
        }

        context.Html = html;
        return Task.FromResult(context);
    }

    [GeneratedRegex(@"src=""assets/([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AssetsSrcRegex();

    [GeneratedRegex(@"<img[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FullImgTagRegex();
}
