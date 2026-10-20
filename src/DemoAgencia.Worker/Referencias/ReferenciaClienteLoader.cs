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
                var estrategia = MontarEstrategia(cliente, tipos);
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
    }

    private static string NormalizarTipoEstrategia(string nomeArquivo)
    {
        var lower = nomeArquivo.ToLowerInvariant();
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

    private static EstrategiaCliente? MontarEstrategia(string cliente, Dictionary<string, string> tipos)
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

        if (tipos.TryGetValue("paleta", out var paletaJson))
            ProcessarPaleta(estrategia, paletaJson);

        if (tipos.TryGetValue("temas", out var temasJson))
            ProcessarTemas(estrategia, temasJson);

        if (tipos.TryGetValue("jornada", out var jornadaJson))
            ProcessarJornada(estrategia, jornadaJson);

        if (tipos.TryGetValue("mapa", out var mapaJson))
            ProcessarMapa(estrategia, mapaJson);

        if (tipos.TryGetValue("regua", out var reguaJson))
            ProcessarRegua(estrategia, reguaJson);

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

    private static void ProcessarPaleta(EstrategiaCliente estrategia, string json)
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
                            if (!string.IsNullOrEmpty(s)) linha.Add(s);
                        }
                        faseEstrategia.CoresHex.Add(linha);
                    }
                }
            }
        }
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

    private static void ProcessarRegua(EstrategiaCliente estrategia, string json)
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

        estrategia.Regua = regua;
    }

    private static void ProcessarFases(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
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
