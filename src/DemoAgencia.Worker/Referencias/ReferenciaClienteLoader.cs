namespace DemoAgencia.Worker.Referencias;

public class ReferenciaClienteLoader : IHostedService, IReferenciasCliente
{
    private readonly ILogger<ReferenciaClienteLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _customPath;
    private readonly Dictionary<string, List<string>> _referenciasPorCliente = new();
    private readonly Dictionary<string, List<string>> _imagensPorCliente = new();
    private readonly Dictionary<string, List<AssetVisual>> _assetsPorCliente = new();
    private readonly int _maxCharsPorArquivo;

    private static readonly HashSet<string> ExtencoesImagem = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    private static readonly HashSet<string> ExtencoesTexto = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".html", ".htm", ".md", ".txt", ".css"
    };

    private static readonly HashSet<string> TiposConhecidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "header", "footer", "icon", "logo", "foto", "post"
    };

    private static int _assetIdCounter;

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
                var idxPrimeiroUnderscore = fileName.IndexOf('_');
                if (idxPrimeiroUnderscore <= 0)
                {
                    _logger.LogWarning("Arquivo sem sufixo ignorado: {File}", fileName);
                    continue;
                }

                var cliente = fileName.Substring(0, idxPrimeiroUnderscore).ToLowerInvariant();
                var restante = fileName.Substring(idxPrimeiroUnderscore + 1);
                var extensao = Path.GetExtension(file);
                var nomeSemExt = Path.GetFileNameWithoutExtension(restante);

                if (ExtencoesImagem.Contains(extensao))
                {
                    if (!_imagensPorCliente.ContainsKey(cliente))
                        _imagensPorCliente[cliente] = new List<string>();
                    _imagensPorCliente[cliente].Add(file);

                    var (tipo, nome) = ParseTipoENome(nomeSemExt);
                    var asset = new AssetVisual
                    {
                        Id = $"asset_{Interlocked.Increment(ref _assetIdCounter)}",
                        Cliente = cliente,
                        Tipo = tipo,
                        Nome = nome,
                        Caminho = file
                    };
                    if (!_assetsPorCliente.ContainsKey(cliente))
                        _assetsPorCliente[cliente] = new List<AssetVisual>();
                    _assetsPorCliente[cliente].Add(asset);
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

        _logger.LogInformation("Referencias carregadas: {Clientes} clientes, {Textos} arquivos texto, {Imagens} imagens, {Assets} assets",
            _referenciasPorCliente.Keys.Union(_imagensPorCliente.Keys).Count(),
            _referenciasPorCliente.Values.Sum(v => v.Count),
            _imagensPorCliente.Values.Sum(v => v.Count),
            _assetsPorCliente.Values.Sum(v => v.Count));

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

    public virtual IReadOnlyCollection<AssetVisual> ListarAssets(string cliente)
    {
        if (!_assetsPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var assets))
            return Array.Empty<AssetVisual>().ToList().AsReadOnly();

        return assets.AsReadOnly();
    }

    public virtual IReadOnlyCollection<string> ListarClientes()
    {
        var todos = _referenciasPorCliente.Keys
            .Union(_imagensPorCliente.Keys)
            .Union(_assetsPorCliente.Keys)
            .Select(c => c.ToLowerInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        return todos.AsReadOnly();
    }

    private static (TipoAsset tipo, string nome) ParseTipoENome(string nomeSemExt)
    {
        var partes = nomeSemExt.Split('_', 2);
        if (partes.Length >= 2 && TiposConhecidos.Contains(partes[0]))
        {
            return (AssetVisual.ParseTipo(partes[0]), partes[1]);
        }

        if (partes.Length == 1 && TiposConhecidos.Contains(partes[0]))
        {
            return (AssetVisual.ParseTipo(partes[0]), partes[0]);
        }

        return (TipoAsset.Outro, nomeSemExt);
    }
}
