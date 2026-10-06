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
            .Where(f => !Path.GetDirectoryName(f)?.Replace(Path.DirectorySeparatorChar, '/')
                .Contains("/estrategia", StringComparison.OrdinalIgnoreCase) ?? true)
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

        _logger.LogInformation("Referencias carregadas: {Clientes} clientes, {Textos} arquivos texto, {Assets} assets, {Estrategias} estrategias",
            _referenciasPorCliente.Keys.Union(_assetsPorCliente.Keys).Union(_estrategiaPorCliente.Keys).Count(),
            _referenciasPorCliente.Values.Sum(v => v.Count),
            _assetsPorCliente.Values.Sum(v => v.Count),
            _estrategiaPorCliente.Count);

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

    private static string NormalizarTipoEstrategia(string nomeArquivo)
    {
        var lower = nomeArquivo.ToLowerInvariant();
        if (lower.Contains("paleta") || lower.Contains("cor"))
            return "paleta";
        if (lower.Contains("tema"))
            return "temas";
        if (lower.Contains("mapa_emocional") || lower.Contains("emocional"))
            return "mapa";
        if (lower.Contains("satisfac") || lower.Contains("insatisfac"))
            return "satisfacoes";
        if (lower.Contains("jornada_cliente") || lower.Contains("jornada"))
            return "jornada";
        if (lower.Contains("tom_verbal") || lower.Contains("tom") || lower.Contains("verbal") || lower.Contains("visual") || lower.Contains("identidade"))
            return "identidade";
        return "";
    }

    private static EstrategiaCliente? MontarEstrategia(string cliente, Dictionary<string, string> tipos)
    {
        var estrategia = new EstrategiaCliente { Cliente = cliente };

        if (tipos.TryGetValue("paleta", out var paletaJson))
            ProcessarPaleta(estrategia, paletaJson);

        if (tipos.TryGetValue("temas", out var temasJson))
            ProcessarTemas(estrategia, temasJson);

        if (tipos.TryGetValue("jornada", out var jornadaJson))
            ProcessarJornada(estrategia, jornadaJson);

        if (tipos.TryGetValue("mapa", out var mapaJson))
            ProcessarMapa(estrategia, mapaJson);

        if (tipos.TryGetValue("satisfacoes", out var satJson))
            ProcessarSatisfacoes(estrategia, satJson);

        if (tipos.TryGetValue("identidade", out var identidadeJson))
            ProcessarIdentidade(estrategia, identidadeJson);

        return estrategia.Fases.Count > 0 || estrategia.MapaEmocional.Count > 0 || estrategia.Identidade != null
            ? estrategia
            : null;
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
            var fase = FaseJornada.Normalizar(etapaRaw);
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
            var fase = FaseJornada.Normalizar(nome);
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
                var faseDestino = InferirFaseDeSubJornada(subJornadaNome);

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

    private static string? InferirFaseDeSubJornada(string nomeSubJornada)
    {
        var lower = nomeSubJornada.ToLowerInvariant();
        if (lower.Contains("pos-compra") || lower.Contains("pós-compra"))
            return FaseJornada.PosCompra;
        if (lower.Contains("pre-chaves") || lower.Contains("pré-chaves"))
            return FaseJornada.PreChaves;
        if (lower.Contains("pos-chaves") || lower.Contains("pós-chaves") || lower.Contains("morador"))
            return FaseJornada.PosChaves;
        return null;
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

    private static void ProcessarSatisfacoes(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("insatisfacoes", out var insatObj) && insatObj.ValueKind == JsonValueKind.Object)
            estrategia.Insatisfacoes = ExtrairCategorias(insatObj);

        if (root.TryGetProperty("satisfacoes", out var satObj) && satObj.ValueKind == JsonValueKind.Object)
            estrategia.Satisfacoes = ExtrairCategorias(satObj);
    }

    private static void ProcessarIdentidade(EstrategiaCliente estrategia, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        estrategia.Identidade = new IdentidadeVerbalVisual
        {
            IdentidadeVerbal = root.TryGetProperty("Identidade verbal", out var iv) ? iv.GetString() ?? "" : "",
            IdentidadeVisual = root.TryGetProperty("Identidade visual", out var ivi) ? ivi.GetString() ?? "" : "",
            TomDeVoz = root.TryGetProperty("Tom de voz", out var tv) ? tv.GetString() ?? "" : "",
            Linguagem = root.TryGetProperty("Linguagem", out var l) ? l.GetString() ?? "" : ""
        };
    }

    private static List<CategoriaSatisfacao> ExtrairCategorias(JsonElement obj)
    {
        var resultado = new List<CategoriaSatisfacao>();
        if (!obj.TryGetProperty("categorias", out var catsArr) || catsArr.ValueKind != JsonValueKind.Array)
            return resultado;

        foreach (var item in catsArr.EnumerateArray())
        {
            var nome = item.TryGetProperty("nome", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(nome)) continue;

            var categoria = new CategoriaSatisfacao { Nome = nome };
            if (item.TryGetProperty("itens", out var itensArr) && itensArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in itensArr.EnumerateArray())
                {
                    var v = i.GetString();
                    if (!string.IsNullOrEmpty(v)) categoria.Itens.Add(v);
                }
            }
            resultado.Add(categoria);
        }

        return resultado;
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

    public virtual AssetVisual? SelecionarBanner(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto)
    {
        var ranked = SelecionarBannersRanked(cliente, fase, subJornada, templateSubJornadas, texto, 1);
        return ranked.Count > 0 ? ranked[0] : null;
    }

    public virtual IReadOnlyList<AssetVisual> SelecionarBannersRanked(string cliente, string? fase, string? subJornada, IReadOnlyList<string>? templateSubJornadas, string? texto, int max)
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

        var scored = banners.Select(b =>
        {
            var nomeNorm = b.Nome.Replace('_', '-').ToLowerInvariant();
            var score = 0;

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
                var stageNorm = FaseJornada.Normalizar(stageName) ?? "";
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

            return (banner: b, score);
        });

        return scored
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.banner.Nome, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.banner)
            .ToList();
    }
}
