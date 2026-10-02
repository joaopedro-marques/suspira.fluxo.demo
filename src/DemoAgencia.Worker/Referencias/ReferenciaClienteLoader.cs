namespace DemoAgencia.Worker.Referencias;

public class ReferenciaClienteLoader : IHostedService, IReferenciasCliente
{
    private readonly ILogger<ReferenciaClienteLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _customPath;
    private readonly Dictionary<string, List<string>> _referenciasPorCliente = new();
    private readonly Dictionary<string, List<string>> _imagensPorCliente = new();
    private readonly int _maxCharsPorArquivo;

    private static readonly HashSet<string> ExtencoesImagem = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    private static readonly HashSet<string> ExtencoesTexto = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".html", ".htm", ".md", ".txt", ".css"
    };

    public ReferenciaClienteLoader(
        ILogger<ReferenciaClienteLoader> logger,
        IConfiguration configuration,
        string? customPath = null)
    {
        _logger = logger;
        _configuration = configuration;
        _customPath = customPath;
        _maxCharsPorArquivo = 4000;
        try
        {
            _maxCharsPorArquivo = _configuration.GetValue<int>("Pipeline:Referencias:MaxCharsPorArquivo", 4000);
        }
        catch
        {
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var assetsPath = _customPath ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "referencias");

        if (!Directory.Exists(assetsPath))
        {
            assetsPath = _customPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "referencias");
        }

        if (!Directory.Exists(assetsPath))
        {
            _logger.LogWarning("Diretorio de referencias nao encontrado: {Path}", assetsPath);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Carregando referencias de: {Path}", assetsPath);

        foreach (var file in Directory.GetFiles(assetsPath))
        {
            try
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("CLIENTE_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var semPrefixo = fileName.Substring("CLIENTE_".Length);
                var idxPrimeiroUnderscore = semPrefixo.IndexOf('_');
                if (idxPrimeiroUnderscore <= 0)
                {
                    _logger.LogWarning("Arquivo sem sufixo de tipo ignorado: {File}", fileName);
                    continue;
                }

                var cliente = semPrefixo.Substring(0, idxPrimeiroUnderscore).ToLowerInvariant();
                var extensao = Path.GetExtension(file);

                if (ExtencoesImagem.Contains(extensao))
                {
                    if (!_imagensPorCliente.ContainsKey(cliente))
                        _imagensPorCliente[cliente] = new List<string>();
                    _imagensPorCliente[cliente].Add(file);
                }
                else if (ExtencoesTexto.Contains(extensao))
                {
                    if (!_referenciasPorCliente.ContainsKey(cliente))
                        _referenciasPorCliente[cliente] = new List<string>();
                    _referenciasPorCliente[cliente].Add(file);
                }
                else
                {
                    _logger.LogWarning("Extensao nao suportada ignorada: {File}", fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar referencia de {File}", file);
            }
        }

        _logger.LogInformation("Referencias carregadas: {Clientes} clientes, {Textos} arquivos texto, {Imagens} imagens",
            _referenciasPorCliente.Keys.Union(_imagensPorCliente.Keys).Count(),
            _referenciasPorCliente.Values.Sum(v => v.Count),
            _imagensPorCliente.Values.Sum(v => v.Count));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual string ObterReferenciasTexto(string cliente)
    {
        if (!_referenciasPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var arquivos))
            return string.Empty;

        var blocos = new List<string>();
        foreach (var arquivo in arquivos)
        {
            var conteudo = File.ReadAllText(arquivo);
            var nomeArquivo = Path.GetFileName(arquivo);

            if (conteudo.Length > _maxCharsPorArquivo)
            {
                conteudo = conteudo.Substring(0, _maxCharsPorArquivo) + "\n... [truncado]";
            }

            blocos.Add($"## {nomeArquivo}\n```\n{conteudo}\n```");
        }

        return string.Join("\n\n", blocos);
    }

    public virtual IReadOnlyCollection<string> ListarImagens(string cliente)
    {
        if (!_imagensPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var imagens))
            return Array.Empty<string>().ToList().AsReadOnly();

        return imagens.AsReadOnly();
    }
}
