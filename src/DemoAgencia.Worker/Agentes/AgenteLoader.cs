namespace DemoAgencia.Worker.Agentes;

public class AgenteLoader : IHostedService, IAgentesCatalogo
{
    private readonly ILogger<AgenteLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, AgenteDefinicao> _agentesPorComando = new();
    private readonly Dictionary<string, AgenteDefinicao> _agentesPorNome = new();
    private readonly Dictionary<string, AgenteDefinicao> _agentesPorPapel = new();
    private readonly string? _customPath;

    public AgenteLoader(ILogger<AgenteLoader> logger, IConfiguration configuration, string? customPath = null)
    {
        _logger = logger;
        _configuration = configuration;
        _customPath = customPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var assetsPath = _customPath ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "agentes");
        
        if (!Directory.Exists(assetsPath))
        {
            assetsPath = _customPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "agentes");
        }

        if (!Directory.Exists(assetsPath))
        {
            _logger.LogWarning("Diretório de agentes não encontrado: {Path}", assetsPath);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Carregando agentes de: {Path}", assetsPath);

        foreach (var file in Directory.GetFiles(assetsPath, "*.md"))
        {
            try
            {
                var agente = ParseAgente(file);
                if (agente != null)
                {
                    foreach (var cmd in agente.Comandos)
                    {
                        _agentesPorComando[cmd.ToLowerInvariant()] = agente;
                    }
                    _agentesPorNome[agente.Nome.ToLowerInvariant()] = agente;
                    _agentesPorPapel[agente.Papel.ToLowerInvariant()] = agente;
                    _logger.LogInformation("Agente carregado: {Nome} (papel: {Papel}, comandos: {Comandos})", 
                        agente.Nome, agente.Papel, string.Join(", ", agente.Comandos));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar agente de {File}", file);
            }
        }

        return Task.CompletedTask;
    }

    private AgenteDefinicao? ParseAgente(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var agente = new AgenteDefinicao();

        var lines = content.Split('\n');
        var inFrontmatter = false;
        var inPersona = false;
        var personaLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == "---")
            {
                inFrontmatter = !inFrontmatter;
                continue;
            }

            if (inFrontmatter)
            {
                if (trimmed.StartsWith("nome:", StringComparison.OrdinalIgnoreCase))
                    agente.Nome = trimmed.Substring(5).Trim();
                else if (trimmed.StartsWith("descricao:", StringComparison.OrdinalIgnoreCase))
                    agente.Descricao = trimmed.Substring(10).Trim();
                else if (trimmed.StartsWith("modelo_alvo:", StringComparison.OrdinalIgnoreCase))
                    agente.ModeloAlvo = trimmed.Substring(12).Trim();
                else if (trimmed.StartsWith("papel:", StringComparison.OrdinalIgnoreCase))
                    agente.Papel = trimmed.Substring(6).Trim().ToLowerInvariant();
                else if (trimmed.StartsWith("interno:", StringComparison.OrdinalIgnoreCase))
                    agente.Interno = trimmed.Substring(8).Trim().ToLowerInvariant() == "true";
                else if (trimmed.StartsWith("temperatura:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(trimmed.Substring(12).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temp))
                        agente.Temperatura = temp;
                }
                else if (trimmed.StartsWith("max_tokens:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(11).Trim(), out var maxTokens))
                        agente.MaxTokens = maxTokens;
                }
                else if (trimmed.StartsWith("- /", StringComparison.OrdinalIgnoreCase))
                    agente.Comandos.Add(trimmed.Substring(2).Trim());
            }
            else if (trimmed.StartsWith("# "))
            {
                inPersona = true;
            }
            else if (inPersona)
            {
                personaLines.Add(line);
            }
        }

        agente.Persona = string.Join("\n", personaLines).Trim();
        return string.IsNullOrEmpty(agente.Nome) ? null : agente;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual AgenteDefinicao? ObterPorComando(string comando)
    {
        return _agentesPorComando.TryGetValue(comando.ToLowerInvariant(), out var agente) ? agente : null;
    }

    public virtual AgenteDefinicao? ObterPorNome(string nome)
    {
        return _agentesPorNome.TryGetValue(nome.ToLowerInvariant(), out var agente) ? agente : null;
    }

    public virtual AgenteDefinicao? ObterPorPapel(string papel)
    {
        return _agentesPorPapel.TryGetValue(papel.ToLowerInvariant(), out var agente) ? agente : null;
    }

    public virtual IReadOnlyCollection<AgenteDefinicao> ListarAgentes()
    {
        return _agentesPorNome.Values.Where(a => !a.Interno).ToList().AsReadOnly();
    }

    public virtual IReadOnlyCollection<AgenteDefinicao> ListarAgentesProducao()
    {
        return _agentesPorNome.Values.Where(a => a.Papel == "producao" && !a.Interno).ToList().AsReadOnly();
    }
}
