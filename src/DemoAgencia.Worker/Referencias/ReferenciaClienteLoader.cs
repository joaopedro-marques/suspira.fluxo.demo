using System.Text.Json;

namespace DemoAgencia.Worker.Referencias;

public class ReferenciaClienteLoader : IHostedService, IReferenciasCliente
{
    private readonly ILogger<ReferenciaClienteLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _customPath;
    private readonly Dictionary<string, List<string>> _referenciasPorCliente = new();
    private readonly Dictionary<string, List<AssetVisual>> _assetsPorCliente = new();
    private readonly Dictionary<string, EstrategiaCliente> _estrategiaPorCliente = new();
    private readonly Dictionary<string, Dictionary<string, string>> _footersPorCliente = new(StringComparer.OrdinalIgnoreCase);
    private string? _heroSectionTemplate;
    private string? _bannerSectionTemplate;
    private string? _headerSectionTemplate;
    private readonly Dictionary<string, string> _headerSectionTemplatesPorCliente = new(StringComparer.OrdinalIgnoreCase);
    private string? _reguaSectionTemplate;
    private readonly Dictionary<string, string> _reguaSectionTemplatesPorCliente = new(StringComparer.OrdinalIgnoreCase);
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
        "header", "footer", "icon", "logo", "foto", "post", "banner"
    };

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "da", "do", "das", "dos", "e", "a", "o", "as", "os",
        "com", "para", "em", "no", "na", "nos", "nas", "por", "pela", "pelo"
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

        foreach (var file in Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var dir = Path.GetDirectoryName(f)?.Replace(Path.DirectorySeparatorChar, '/') ?? "";
                return !dir.Contains("/estrategia", StringComparison.OrdinalIgnoreCase)
                    && !dir.Contains("/footer", StringComparison.OrdinalIgnoreCase);
            })
            .Where(f => !f.EndsWith(".desc.json", StringComparison.OrdinalIgnoreCase)))
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
                    var (tipo, nome) = ParseTipoENome(nomeSemExt);
                    if (tipo == TipoAsset.Outro && PathInSubdir(file, assetsPath, "banners"))
                        tipo = TipoAsset.Banner;
                    if (tipo == TipoAsset.Outro && PathInSubdir(file, assetsPath, "icons"))
                        tipo = TipoAsset.Icon;
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

        CarregarEstrategias(assetsPath);
        CarregarFooters(assetsPath);
        CarregarComponentes(assetsPath);

        _logger.LogInformation("Referencias carregadas: {Clientes} clientes, {Textos} arquivos texto, {Assets} assets, {Estrategias} estrategias, {Footers} footers",
            _referenciasPorCliente.Keys.Union(_assetsPorCliente.Keys).Union(_estrategiaPorCliente.Keys).Union(_footersPorCliente.Keys).Count(),
            _referenciasPorCliente.Values.Sum(v => v.Count),
            _assetsPorCliente.Values.Sum(v => v.Count),
            _estrategiaPorCliente.Count,
            _footersPorCliente.Values.Sum(v => v.Count));

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

    public virtual IReadOnlyCollection<AssetVisual> ListarAssets(string cliente)
    {
        if (!_assetsPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var assets))
            return Array.Empty<AssetVisual>().ToList().AsReadOnly();

        return assets.AsReadOnly();
    }

    public virtual IReadOnlyCollection<string> ListarClientes()
    {
        var todos = _referenciasPorCliente.Keys
            .Union(_assetsPorCliente.Keys)
            .Union(_estrategiaPorCliente.Keys)
            .Union(_footersPorCliente.Keys)
            .Select(c => c.ToLowerInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        return todos.AsReadOnly();
    }

    public virtual EstrategiaCliente? ObterEstrategia(string cliente)
    {
        if (!_estrategiaPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var estrategia))
            return null;

        return estrategia;
    }

    public virtual string? ObterFooterHtml(string cliente, string? nome = null)
    {
        if (string.IsNullOrEmpty(cliente))
            return null;

        var clienteKey = cliente.ToLowerInvariant();
        if (!_footersPorCliente.TryGetValue(clienteKey, out var footers))
            return null;

        // If a specific name is requested, only return that exact footer (no fallback)
        if (!string.IsNullOrEmpty(nome))
        {
            return footers.TryGetValue(nome.ToLowerInvariant(), out var namedFooter) ? namedFooter : null;
        }

        // If no name is specified, return the default footer
        return footers.TryGetValue("", out var defaultFooter) ? defaultFooter : null;
    }

    public virtual string? ObterHeroSectionTemplate()
    {
        return _heroSectionTemplate;
    }

    public virtual string? ObterBannerSectionTemplate()
    {
        return _bannerSectionTemplate;
    }

    public virtual string? ObterHeaderSectionTemplate(string? cliente = null)
    {
        if (!string.IsNullOrWhiteSpace(cliente)
            && _headerSectionTemplatesPorCliente.TryGetValue(cliente.Trim(), out var especifico))
        {
            return especifico;
        }

        return _headerSectionTemplate;
    }

    public virtual string? ObterReguaSectionTemplate(string? cliente = null)
    {
        if (!string.IsNullOrWhiteSpace(cliente)
            && _reguaSectionTemplatesPorCliente.TryGetValue(cliente.Trim(), out var especifico))
        {
            return especifico;
        }

        return _reguaSectionTemplate;
    }

    private void CarregarEstrategias(string assetsPath)
    {
        var estrategiaPath = Path.Combine(assetsPath, "estrategia");
        if (!Directory.Exists(estrategiaPath))
        {
            _logger.LogDebug("Subpasta estrategia/ nao encontrada em: {Path}", assetsPath);
            return;
        }

        var estrategiaPorClienteETipo = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(estrategiaPath, "*.json"))
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var idxPrimeiroUnderscore = fileName.IndexOf('_');
                if (idxPrimeiroUnderscore <= 0)
                    continue;

                var cliente = fileName.Substring(0, idxPrimeiroUnderscore).ToLowerInvariant();
                var restante = Path.GetFileNameWithoutExtension(fileName.Substring(idxPrimeiroUnderscore + 1));
                var tipo = NormalizarTipoEstrategia(restante);
                if (string.IsNullOrEmpty(tipo))
                {
                    _logger.LogWarning("Tipo de estrategia desconhecido: {File}", fileName);
                    continue;
                }

                if (!estrategiaPorClienteETipo.ContainsKey(cliente))
                    estrategiaPorClienteETipo[cliente] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                estrategiaPorClienteETipo[cliente][tipo] = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar estrategia de {File}", file);
            }
        }

        foreach (var (cliente, tipos) in estrategiaPorClienteETipo)
        {
            try
            {
                var estrategia = MontarEstrategia(cliente, tipos, _logger);
                if (estrategia != null)
                {
                    _estrategiaPorCliente[cliente] = estrategia;
                    _logger.LogInformation("Estrategia carregada para cliente {Cliente}: {Fases} fases, {EtapasEmocionais} etapas emocionais",
                        cliente, estrategia.Fases.Count, estrategia.MapaEmocional.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao montar estrategia do cliente {Cliente}", cliente);
            }
        }
    }

    private void CarregarFooters(string assetsPath)
    {
        var footerPath = Path.Combine(assetsPath, "footer");
        if (!Directory.Exists(footerPath))
        {
            _logger.LogDebug("Subpasta footer/ nao encontrada em: {Path}", assetsPath);
            return;
        }

        foreach (var file in Directory.GetFiles(footerPath, "*.html")
            .Concat(Directory.GetFiles(footerPath, "*.htm")))
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var idxPrimeiroUnderscore = fileName.IndexOf('_');
                if (idxPrimeiroUnderscore <= 0)
                {
                    _logger.LogWarning("Arquivo de footer sem sufixo de cliente ignorado: {File}", fileName);
                    continue;
                }

                var cliente = fileName.Substring(0, idxPrimeiroUnderscore).ToLowerInvariant();
                var restante = Path.GetFileNameWithoutExtension(fileName.Substring(idxPrimeiroUnderscore + 1));

                // Parse nome do footer: "footer" = default, "footer_nome" = nomeado
                string nome;
                if (restante.Equals("footer", StringComparison.OrdinalIgnoreCase))
                {
                    nome = "";
                }
                else if (restante.StartsWith("footer_", StringComparison.OrdinalIgnoreCase))
                {
                    nome = restante.Substring("footer_".Length).ToLowerInvariant();
                }
                else
                {
                    _logger.LogWarning("Arquivo de footer com nome inesperado ignorado: {File}", fileName);
                    continue;
                }

                var html = File.ReadAllText(file);

                if (!_footersPorCliente.ContainsKey(cliente))
                    _footersPorCliente[cliente] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                _footersPorCliente[cliente][nome] = html;
                _logger.LogDebug("Footer carregado para cliente {Cliente}: {Nome}",
                    cliente, string.IsNullOrEmpty(nome) ? "default" : nome);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar footer de {File}", file);
            }
        }

        _logger.LogInformation("Footers carregados: {Clientes} clientes, {Total} footers",
            _footersPorCliente.Count,
            _footersPorCliente.Values.Sum(v => v.Count));
    }

    private void CarregarComponentes(string assetsPath)
    {
        var componentsPath = Path.Combine(assetsPath, "components");
        if (!Directory.Exists(componentsPath))
        {
            _logger.LogDebug("Subpasta components/ nao encontrada em: {Path}", assetsPath);
            return;
        }

        var heroFile = Path.Combine(componentsPath, "hero_section.html");
        if (File.Exists(heroFile))
        {
            _heroSectionTemplate = File.ReadAllText(heroFile);
            _logger.LogDebug("Template hero_section.html carregado");
        }

        var bannerFile = Path.Combine(componentsPath, "banner_section.html");
        if (File.Exists(bannerFile))
        {
            _bannerSectionTemplate = File.ReadAllText(bannerFile);
            _logger.LogDebug("Template banner_section.html carregado");
        }

        CarregarTemplatesPorCliente(
            componentsPath,
            "header_section",
            "header",
            template => _headerSectionTemplate = template,
            _headerSectionTemplatesPorCliente);

        CarregarTemplatesPorCliente(
            componentsPath,
            "regua_section",
            "regua",
            template => _reguaSectionTemplate = template,
            _reguaSectionTemplatesPorCliente);
    }

    /// <summary>
    /// Carrega componentes no padrao <c>{prefixo}.html</c> (global) e
    /// <c>{prefixo}_{CLIENTE}.html</c> (override por cliente).
    /// </summary>
    private void CarregarTemplatesPorCliente(
        string componentsPath,
        string prefixo,
        string descricao,
        Action<string> definirGlobal,
        IDictionary<string, string> porCliente)
    {
        foreach (var arquivo in Directory.GetFiles(componentsPath, $"{prefixo}*.html"))
        {
            var nome = Path.GetFileNameWithoutExtension(arquivo);
            var conteudo = File.ReadAllText(arquivo);

            if (nome.Equals(prefixo, StringComparison.OrdinalIgnoreCase))
            {
                definirGlobal(conteudo);
                _logger.LogDebug("Template {Prefixo}.html carregado", prefixo);
                continue;
            }

            if (!nome.StartsWith($"{prefixo}_", StringComparison.OrdinalIgnoreCase))
                continue;

            var sufixo = nome[(prefixo.Length + 1)..];
            if (sufixo.Length == 0)
                continue;

            porCliente[sufixo] = conteudo;
            _logger.LogDebug("Template de {Descricao} especifico carregado: {Nome}", descricao, nome);
        }
    }

    private static string NormalizarTipoEstrategia(string nomeArquivo)
    {
        var lower = nomeArquivo.ToLowerInvariant();
        if (lower == "header")
            return "header";
        if (lower.Contains("fases"))
            return "fases";
        if (lower.Contains("regua"))
            return "regua";
        if (lower.Contains("paleta") || lower.Contains("cor"))
            return "paleta";
        if (lower.Contains("tema"))
            return "temas";
        if (lower.Contains("mapa_emocional") || lower.Contains("emocional"))
            return "mapa";
        if (lower.Contains("jornada_cliente") || lower.Contains("jornada"))
            return "jornada";
        return "";
    }

    private static EstrategiaCliente? MontarEstrategia(string cliente, Dictionary<string, string> tipos, ILogger logger)
    {
        var estrategia = new EstrategiaCliente { Cliente = cliente };

        // Processar fases PRIMEIRO para ter o vocabulário disponível
        if (tipos.TryGetValue("fases", out var fasesJson))
            ProcessarFases(estrategia, fasesJson);

        // Se não há configuração de fases, criar uma padrão baseada nas fases encontradas
        if (estrategia.ConfiguracaoFases == null)
        {
            estrategia.ConfiguracaoFases = CriarConfiguracaoFasesPadrao(tipos);
        }

        // O header e lido antes da paleta para a validacao de contraste dos assets
        // considerar overrides por fase (ex.: fundo hero com cor de fallback).
        if (tipos.TryGetValue("header", out var headerJson))
            ProcessarHeader(estrategia, headerJson, logger);

        if (tipos.TryGetValue("paleta", out var paletaJson))
            ProcessarPaleta(estrategia, paletaJson, logger);

        if (tipos.TryGetValue("temas", out var temasJson))
            ProcessarTemas(estrategia, temasJson);

        if (tipos.TryGetValue("jornada", out var jornadaJson))
            ProcessarJornada(estrategia, jornadaJson);

        if (tipos.TryGetValue("mapa", out var mapaJson))
            ProcessarMapa(estrategia, mapaJson);

        if (tipos.TryGetValue("regua", out var reguaJson))
            ProcessarRegua(estrategia, reguaJson, logger);

        return estrategia.Fases.Count > 0 || estrategia.MapaEmocional.Count > 0 || estrategia.Regua != null || estrategia.ConfiguracaoFases != null
            ? estrategia
            : null;
    }

    private static FasesConfig CriarConfiguracaoFasesPadrao(Dictionary<string, string> tipos)
    {
        var config = new FasesConfig();
        var fasesEncontradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extrair fases do arquivo de paleta
        if (tipos.TryGetValue("paleta", out var paletaJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(paletaJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("paleta_de_cores", out var paletaArr) && paletaArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in paletaArr.EnumerateArray())
                    {
                        var etapa = item.TryGetProperty("etapa", out var et) ? et.GetString() : null;
                        if (!string.IsNullOrEmpty(etapa))
                        {
                            // Usar FaseJornada.Normalizar para extrair a fase padrão
                            var faseNormalizada = FaseJornada.Normalizar(etapa);
                            if (!string.IsNullOrEmpty(faseNormalizada))
                            {
                                fasesEncontradas.Add(faseNormalizada);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Extrair fases do arquivo de temas
        if (tipos.TryGetValue("temas", out var temasJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(temasJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("fases", out var fasesArr) && fasesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in fasesArr.EnumerateArray())
                    {
                        var nome = item.TryGetProperty("nome", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(nome))
                        {
                            // Usar FaseJornada.Normalizar para extrair a fase padrão
                            var faseNormalizada = FaseJornada.Normalizar(nome);
                            if (!string.IsNullOrEmpty(faseNormalizada))
                            {
                                fasesEncontradas.Add(faseNormalizada);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Criar FaseDefinicao para cada fase encontrada
        foreach (var fase in fasesEncontradas)
        {
            config.Fases[fase] = new FaseDefinicao { FaseKey = fase };
        }

        return config.Fases.Count > 0 ? config : new FasesConfig();
    }

    private static void ProcessarPaleta(EstrategiaCliente estrategia, string json, ILogger logger)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("paleta_de_cores", out var paletaArr) || paletaArr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in paletaArr.EnumerateArray())
        {
            var corPrincipal = item.TryGetProperty("cor_principal", out var cp) ? cp.GetString() : null;
            var etapaRaw = item.TryGetProperty("etapa", out var et) ? et.GetString() : null;
            var descricao = item.TryGetProperty("descricao", out var desc) ? desc.GetString() : null;
            var fase = estrategia.NormalizarFase(etapaRaw);
            if (string.IsNullOrEmpty(fase))
                continue;

            var faseEstrategia = ObterOuCriarFase(estrategia, fase);
            faseEstrategia.CorPrincipal = corPrincipal;
            faseEstrategia.DescricaoCor = descricao;
            faseEstrategia.CorTextoHeader = LerCorHexDoJson(item, "cor_texto_header", estrategia.Cliente, fase, logger);

            if (item.TryGetProperty("cores_hex_aproximadas", out var hexArr) && hexArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var hexItem in hexArr.EnumerateArray())
                {
                    if (hexItem.ValueKind == JsonValueKind.Array)
                    {
                        var linha = new List<string>();
                        foreach (var hex in hexItem.EnumerateArray())
                        {
                            var s = hex.GetString();
                            if (string.IsNullOrEmpty(s))
                                continue;

                            if (!s.StartsWith('#'))
                            {
                                linha.Add(s);
                                continue;
                            }

                            if (CorHex.TryNormalizar(s, out var normalizada))
                                linha.Add(normalizada);
                            else
                                logger.LogWarning(
                                    "Cor hex invalida descartada na paleta de {Cliente}/{Fase}: {Cor}",
                                    estrategia.Cliente, fase, s);
                        }
                        faseEstrategia.CoresHex.Add(linha);
                    }
                }
            }

            ValidarCoresHeader(estrategia, faseEstrategia, logger);
        }
    }

    private static string? LerCorHexDoJson(JsonElement item, string propriedade, string cliente, string fase, ILogger logger)
    {
        if (!item.TryGetProperty(propriedade, out var valor) || valor.ValueKind != JsonValueKind.String)
            return null;

        var bruto = valor.GetString();
        if (string.IsNullOrWhiteSpace(bruto))
            return null;

        if (CorHex.TryNormalizar(bruto, out var cor))
            return cor;

        logger.LogWarning(
            "Cor invalida em {Propriedade} da paleta de {Cliente}/{Fase}: {Cor}. Use #RGB ou #RRGGBB.",
            propriedade, cliente, fase, bruto);
        return null;
    }

    /// <summary>
    /// Avisa quando uma cor declarada para o header fica ilegivel sobre o fundo do gradiente.
    /// O build nao falha e o renderer nao altera a cor: o teste de contrato dos assets acusa.
    /// </summary>
    private static void ValidarCoresHeader(EstrategiaCliente estrategia, FaseEstrategia fase, ILogger logger)
    {
        var fundos = FundosEfetivosHeader(estrategia, fase);
        if (fundos.Count == 0)
            return;

        if (fase.CorTextoHeader != null)
            ValidarContraste(estrategia.Cliente, fase, "cor_texto_header", fase.CorTextoHeader, fundos, logger);
    }

    /// <summary>
    /// Fundos usados na validacao. Fases com fundo hero sao validadas contra a cor solida
    /// de fallback: a foto + scrim e o fundo real, mas quem ignora background-image
    /// (Outlook desktop) ve essa cor.
    /// </summary>
    private static IReadOnlyCollection<string> FundosEfetivosHeader(EstrategiaCliente estrategia, FaseEstrategia fase)
    {
        var faseHeader = estrategia.Header?.ObterFase(fase.Fase);
        if (faseHeader?.UsaHeroComoFundo == true && faseHeader.CorFundo is { } corFundo)
            return new[] { corFundo };

        return CoresFase.Gradiente(fase);
    }

    private static void ValidarContraste(
        string cliente,
        FaseEstrategia fase,
        string propriedade,
        string cor,
        IReadOnlyCollection<string> fundos,
        ILogger logger)
    {
        var contraste = CorHex.ContrasteMinimo(fundos, cor);
        if (contraste >= CorHex.ContrasteMinimoAbsoluto)
            return;

        logger.LogWarning(
            "{Propriedade} de {Cliente}/{Fase} ({Cor}) tem contraste {Contraste:0.00}:1 sobre o fundo do header. Minimo {Minimo:0.0}:1; a cor declarada sera usada como esta.",
            propriedade, cliente, fase.Fase, cor, contraste, CorHex.ContrasteMinimoAbsoluto);
    }

    private static void ProcessarTemas(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fases", out var fasesArr) || fasesArr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in fasesArr.EnumerateArray())
        {
            var nome = item.TryGetProperty("nome", out var n) ? n.GetString() : null;
            var fase = estrategia.NormalizarFase(nome);
            if (string.IsNullOrEmpty(fase))
                continue;

            var faseEstrategia = ObterOuCriarFase(estrategia, fase);
            if (item.TryGetProperty("temas", out var temasArr) && temasArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var tema in temasArr.EnumerateArray())
                {
                    var t = tema.GetString();
                    if (!string.IsNullOrEmpty(t))
                        faseEstrategia.Temas.Add(t);
                }
            }
        }
    }

    private static void ProcessarJornada(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var faseProp in root.EnumerateObject())
        {
            if (faseProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var subJornadaProp in faseProp.Value.EnumerateObject())
            {
                if (subJornadaProp.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var subJornadaNome = subJornadaProp.Name;
                var faseDestino = InferirFaseDeSubJornada(subJornadaNome, estrategia);

                if (string.IsNullOrEmpty(faseDestino))
                    continue;

                var faseEstrategia = ObterOuCriarFase(estrategia, faseDestino);
                if (!faseEstrategia.SubJornadas.ContainsKey(subJornadaNome))
                    faseEstrategia.SubJornadas[subJornadaNome] = new List<string>();

                foreach (var itemProp in subJornadaProp.Value.EnumerateObject())
                {
                    var valor = itemProp.Value.GetString();
                    if (!string.IsNullOrEmpty(valor))
                        faseEstrategia.SubJornadas[subJornadaNome].Add(valor);
                }
            }
        }
    }

    private static string? InferirFaseDeSubJornada(string nomeSubJornada, EstrategiaCliente estrategia)
    {
        return estrategia.NormalizarFase(nomeSubJornada);
    }

    private static void ProcessarMapa(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("jornada", out var jornadaArr) || jornadaArr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in jornadaArr.EnumerateArray())
        {
            var etapa = item.TryGetProperty("etapa", out var e) ? e.GetString() : null;
            var fator = item.TryGetProperty("fator_decisao", out var f) ? f.GetString() : null;

            if (string.IsNullOrEmpty(etapa))
                continue;

            var emocional = new EtapaEmocional
            {
                Etapa = etapa,
                FatorDecisao = fator ?? ""
            };

            if (item.TryGetProperty("sentimentos", out var sentArr) && sentArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sentArr.EnumerateArray())
                {
                    var sv = s.GetString();
                    if (!string.IsNullOrEmpty(sv)) emocional.Sentimentos.Add(sv);
                }
            }

            estrategia.MapaEmocional.Add(emocional);
        }
    }

    private static void ProcessarRegua(EstrategiaCliente estrategia, string json, ILogger logger)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("regua", out var reguaObj))
            return;

        var regua = new ReguaJornada();

        // Extrair etapas
        if (reguaObj.TryGetProperty("etapas", out var etapasArr) && etapasArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var etapa in etapasArr.EnumerateArray())
            {
                var etapaStr = etapa.GetString();
                if (!string.IsNullOrEmpty(etapaStr))
                    regua.Etapas.Add(etapaStr);
            }
        }

        // Extrair mapa de fase para etapa destacada
        if (reguaObj.TryGetProperty("etapa_destacada_por_fase", out var mapaObj) && mapaObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in mapaObj.EnumerateObject())
            {
                var etapaDestacada = prop.Value.GetString();
                if (!string.IsNullOrEmpty(etapaDestacada))
                    regua.EtapaDestacadaPorFase[prop.Name] = etapaDestacada;
            }
        }

        // Extrair estilo
        if (reguaObj.TryGetProperty("estilo", out var estiloObj))
        {
            var estilo = new EstiloRegua();

            if (estiloObj.TryGetProperty("concluida", out var concluida))
                estilo.Concluida = concluida.GetString() ?? estilo.Concluida;

            if (estiloObj.TryGetProperty("atual", out var atual))
                estilo.Atual = atual.GetString() ?? estilo.Atual;

            if (estiloObj.TryGetProperty("futura", out var futura))
                estilo.Futura = futura.GetString() ?? estilo.Futura;

            if (estiloObj.TryGetProperty("diretrizes", out var diretrizesArr) && diretrizesArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var diretriz in diretrizesArr.EnumerateArray())
                {
                    var diretrizStr = diretriz.GetString();
                    if (!string.IsNullOrEmpty(diretrizStr))
                        estilo.Diretrizes.Add(diretrizStr);
                }
            }

            regua.Estilo = estilo;
        }

        // Extrair cores da regua renderizada em HTML no header do email
        if (reguaObj.TryGetProperty("estilo_html", out var estiloHtmlObj))
            regua.EstiloHtml = LerEstiloHtml(estrategia.Cliente, "estilo_html", estiloHtmlObj, logger);

        // Overrides por fase: a regua fica sobre o fundo do header, que muda a cada fase
        if (reguaObj.TryGetProperty("estilo_html_por_fase", out var estiloPorFaseObj)
            && estiloPorFaseObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var faseProp in estiloPorFaseObj.EnumerateObject())
            {
                regua.EstiloHtmlPorFase[faseProp.Name] =
                    LerEstiloHtml(estrategia.Cliente, $"estilo_html_por_fase.{faseProp.Name}", faseProp.Value, logger);
            }
        }

        estrategia.Regua = regua;
    }

    private static EstiloReguaHtml LerEstiloHtml(string cliente, string contexto, JsonElement obj, ILogger logger)
    {
        var padrao = new EstiloReguaHtml();
        var estilo = new EstiloReguaHtml
        {
            CorConcluida = LerCorRegua(obj, "cor_concluida", cliente, contexto, padrao.CorConcluida, logger),
            CorAtual = LerCorRegua(obj, "cor_atual", cliente, contexto, padrao.CorAtual, logger),
            CorLabel = LerCorRegua(obj, "cor_label", cliente, contexto, padrao.CorLabel, logger),
            CorLabelAtual = LerCorRegua(obj, "cor_label_atual", cliente, contexto, padrao.CorLabelAtual, logger)
        };

        // cor_futura pode ser rgba()/transparent (translucida por design); aceita como veio
        if (obj.TryGetProperty("cor_futura", out var futura) && futura.ValueKind == JsonValueKind.String)
        {
            var valor = futura.GetString();
            if (!string.IsNullOrWhiteSpace(valor))
                estilo.CorFutura = valor;
        }

        return estilo;
    }

    private static string LerCorRegua(
        JsonElement obj,
        string propriedade,
        string cliente,
        string contexto,
        string valorPadrao,
        ILogger logger)
    {
        if (!obj.TryGetProperty(propriedade, out var valor) || valor.ValueKind != JsonValueKind.String)
            return valorPadrao;

        var bruto = valor.GetString();
        if (string.IsNullOrWhiteSpace(bruto))
            return valorPadrao;

        if (CorHex.TryNormalizar(bruto, out var cor))
            return cor;

        logger.LogWarning(
            "Cor invalida em {Contexto}.{Propriedade} de {Cliente}: {Cor}. Mantendo {Padrao}.",
            contexto, propriedade, cliente, bruto, valorPadrao);
        return valorPadrao;
    }

    private static void ProcessarHeader(EstrategiaCliente estrategia, string json, ILogger logger)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var header = new HeaderConfig();

        if (root.TryGetProperty("colunas", out var colunas))
        {
            var lista = LerNomesDeBlocos(colunas);
            if (lista.Count > 0)
                header.Colunas = lista;
        }

        if (root.TryGetProperty("sortear_ordem", out var sortear)
            && sortear.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            header.SortearOrdem = sortear.GetBoolean();
        }

        if (root.TryGetProperty("requer_hero", out var requer)
            && requer.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            header.RequerHero = requer.GetBoolean();
        }

        header.ClipPathPadrao = LerTextoHeader(root, "clip_path_padrao", header.ClipPathPadrao, estrategia.Cliente, logger);
        header.ClipPathInvertido = LerTextoHeader(root, "clip_path_invertido", header.ClipPathInvertido, estrategia.Cliente, logger);
        header.GradienteAngulo = LerTextoHeader(root, "gradiente_angulo", header.GradienteAngulo, estrategia.Cliente, logger);

        if (root.TryGetProperty("por_fase", out var porFaseObj) && porFaseObj.ValueKind == JsonValueKind.Object)
            LerOverridesPorFase(estrategia, porFaseObj, header, logger);

        estrategia.Header = header;
    }

    private static void LerOverridesPorFase(
        EstrategiaCliente estrategia,
        JsonElement porFaseObj,
        HeaderConfig header,
        ILogger logger)
    {
        foreach (var faseProp in porFaseObj.EnumerateObject())
        {
            if (faseProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            var config = new HeaderFaseConfig();

            if (faseProp.Value.TryGetProperty("fundo", out var fundo) && fundo.ValueKind == JsonValueKind.String)
            {
                var valor = fundo.GetString()?.Trim().ToLowerInvariant();
                if (valor is "hero" or "gradiente")
                {
                    config.Fundo = valor;
                }
                else
                {
                    logger.LogWarning(
                        "Valor invalido em por_fase.{Fase}.fundo do header de {Cliente}: {Valor}. Use \"hero\" ou \"gradiente\".",
                        faseProp.Name, estrategia.Cliente, fundo.GetString());
                }
            }

            if (faseProp.Value.TryGetProperty("scrim", out var scrim) && scrim.ValueKind == JsonValueKind.String)
            {
                var valor = scrim.GetString();
                if (!string.IsNullOrWhiteSpace(valor))
                    config.Scrim = valor.Trim();
            }

            if (faseProp.Value.TryGetProperty("cor_fundo", out var corFundo) && corFundo.ValueKind == JsonValueKind.String)
            {
                var bruto = corFundo.GetString();
                if (CorHex.TryNormalizar(bruto, out var normalizada))
                {
                    config.CorFundo = normalizada;
                }
                else if (!string.IsNullOrWhiteSpace(bruto))
                {
                    logger.LogWarning(
                        "Cor invalida em por_fase.{Fase}.cor_fundo do header de {Cliente}: {Cor}. Use #RGB ou #RRGGBB.",
                        faseProp.Name, estrategia.Cliente, bruto);
                }
            }

            if (faseProp.Value.TryGetProperty("colunas", out var colunasFase))
            {
                var lista = LerNomesDeBlocos(colunasFase);
                if (lista.Count > 0)
                    config.Colunas = lista;
            }

            if (faseProp.Value.TryGetProperty("sortear_ordem", out var sortearFase)
                && sortearFase.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                config.SortearOrdem = sortearFase.GetBoolean();
            }

            var chave = faseProp.Name.Trim();
            if (chave.Length > 0)
                header.PorFase[chave] = config;
        }
    }

    private static List<string> LerNomesDeBlocos(JsonElement valor)
    {
        var lista = new List<string>();
        if (valor.ValueKind != JsonValueKind.Array)
            return lista;

        foreach (var item in valor.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var nome = item.GetString();
            if (!string.IsNullOrWhiteSpace(nome))
                lista.Add(nome.Trim());
        }

        return lista;
    }

    private static string LerTextoHeader(JsonElement root, string propriedade, string padrao, string cliente, ILogger logger)
    {
        if (!root.TryGetProperty(propriedade, out var valor) || valor.ValueKind != JsonValueKind.String)
            return padrao;

        var texto = valor.GetString();
        if (string.IsNullOrWhiteSpace(texto))
            return padrao;

        var limpo = texto.Trim();
        if (limpo.Contains('<') || limpo.Contains('>') || limpo.Contains('\n') || limpo.Contains('\r'))
        {
            logger.LogWarning(
                "Valor invalido em {Propriedade} do header de {Cliente}; mantendo o padrao.",
                propriedade, cliente);
            return padrao;
        }

        return limpo;
    }

    private static void ProcessarFases(EstrategiaCliente estrategia, string json)
    {        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fases", out var fasesObj) || fasesObj.ValueKind != JsonValueKind.Object)
            return;

        var config = new FasesConfig();

        foreach (var faseProp in fasesObj.EnumerateObject())
        {
            var faseKey = faseProp.Name;
            var definicao = new FaseDefinicao { FaseKey = faseKey };

            if (faseProp.Value.TryGetProperty("sinonimos", out var sinonimosArr) && sinonimosArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var sinonimo in sinonimosArr.EnumerateArray())
                {
                    var sinStr = sinonimo.GetString();
                    if (!string.IsNullOrEmpty(sinStr))
                        definicao.Sinonimos.Add(sinStr);
                }
            }

            config.Fases[faseKey] = definicao;
            
            // Também criar entrada em estrategia.Fases se ainda não existir
            if (!estrategia.Fases.ContainsKey(faseKey))
            {
                estrategia.Fases[faseKey] = new FaseEstrategia { Fase = faseKey };
            }
        }

        estrategia.ConfiguracaoFases = config;
    }

    private static FaseEstrategia ObterOuCriarFase(EstrategiaCliente estrategia, string fase)
    {
        if (!estrategia.Fases.TryGetValue(fase, out var faseEstrategia))
        {
            faseEstrategia = new FaseEstrategia { Fase = fase };
            estrategia.Fases[fase] = faseEstrategia;
        }
        return faseEstrategia;
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

    private static bool PathInSubdir(string file, string basePath, string subdir)
    {
        var relative = Path.GetRelativePath(basePath, file).Replace('\\', '/');
        return relative.StartsWith(subdir + "/", StringComparison.OrdinalIgnoreCase)
            || relative.Contains("/" + subdir + "/", StringComparison.OrdinalIgnoreCase);
    }

    public virtual AssetVisual? SelecionarBanner(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto, string? tema = null)
    {
        var ranked = SelecionarBannersRanked(cliente, fase, subJornada, templateSubJornadas, texto, 1, tema);
        return ranked.Count > 0 ? ranked[0] : null;
    }

    public virtual IReadOnlyList<AssetVisual> SelecionarBannersRanked(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto, int max, string? tema = null)
    {
        if (max <= 0)
            return Array.Empty<AssetVisual>();

        if (!_assetsPorCliente.TryGetValue(cliente.ToLowerInvariant(), out var assets))
            return Array.Empty<AssetVisual>();

        var banners = assets.Where(a => a.Tipo == TipoAsset.Banner).ToList();
        if (banners.Count == 0)
            return Array.Empty<AssetVisual>();

        var textoLower = texto?.ToLowerInvariant() ?? "";
        var subJornadaLower = subJornada?.ToLowerInvariant() ?? "";

        var faseStageNames = new List<string>();
        var estrategia = ObterEstrategia(cliente);
        if (estrategia != null && !string.IsNullOrEmpty(fase)
            && estrategia.Fases.TryGetValue(fase, out var faseDados))
        {
            foreach (var stageList in faseDados.SubJornadas.Values)
                faseStageNames.AddRange(stageList);
        }

        var temaPalavras = ExtrairPalavrasTema(tema);

        var scored = banners.Select(b =>
        {
            var nomeNorm = b.Nome.Replace('_', '-').ToLowerInvariant();
            var score = 0;
            var temaScore = 0;

            foreach (var palavra in temaPalavras)
            {
                if (nomeNorm.Contains(palavra)) temaScore += 10;
            }

            if (templateSubJornadas != null)
            {
                foreach (var sub in templateSubJornadas)
                {
                    var subNorm = sub.ToLowerInvariant();
                    if (nomeNorm.Contains(subNorm))
                        score += 5;
                }
            }

            if (!string.IsNullOrEmpty(subJornadaLower) && nomeNorm.Contains(subJornadaLower))
                score += 5;

            foreach (var stageName in faseStageNames)
            {
                var stageNorm = estrategia?.NormalizarFase(stageName) ?? FaseJornada.NormalizarTexto(stageName);
                if (string.IsNullOrEmpty(stageNorm)) continue;
                var keywords = stageNorm.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var kw in keywords)
                {
                    if (kw.Length > 2 && nomeNorm.Contains(kw))
                    {
                        score += 3;
                        break;
                    }
                }
            }

            var bannerWords = nomeNorm.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in bannerWords)
            {
                if (word.Length > 2 && textoLower.Contains(word))
                    score += 1;
            }

            return (banner: b, score, temaScore);
        });

        var temTema = temaPalavras.Count > 0;

        if (temTema)
        {
            var temMatch = scored.Where(x => x.temaScore > 0).ToList();
            if (temMatch.Count > 0)
            {
                return temMatch
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.banner.Nome, StringComparer.OrdinalIgnoreCase)
                    .Take(max)
                    .Select(x => x.banner)
                    .ToList();
            }

            return Array.Empty<AssetVisual>();
        }

        return scored
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.banner.Nome, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.banner)
            .ToList();
    }

    private static List<string> ExtrairPalavrasTema(string? tema)
    {
        if (string.IsNullOrWhiteSpace(tema)) return new List<string>();

        var normalized = tema.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i").Replace("ó", "o").Replace("ô", "o")
            .Replace("ú", "u").Replace("ã", "a").Replace("õ", "o")
            .Replace("ç", "c").Replace("-", " ").Replace("_", " ");

        return normalized
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .ToList();
    }
}
