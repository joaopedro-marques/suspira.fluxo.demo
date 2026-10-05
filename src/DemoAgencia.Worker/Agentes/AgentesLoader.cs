using System.Globalization;

namespace DemoAgencia.Worker.Agentes;

public class AgentesLoader : IHostedService, IAgentesCatalogo
{
    private readonly ILogger<AgentesLoader> _logger;
    private readonly string? _customPath;
    private readonly Dictionary<string, AgenteDefinicao> _agentes = new(StringComparer.OrdinalIgnoreCase);

    public AgentesLoader(ILogger<AgentesLoader> logger, string? customPath = null)
    {
        _logger = logger;
        _customPath = customPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();

        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Diretorio de agentes nao encontrado: {path}");

        _logger.LogInformation("Carregando agentes de: {Path}", path);

        foreach (var file in Directory.GetFiles(path, "*.md"))
        {
            var nome = Path.GetFileNameWithoutExtension(file);
            var conteudo = File.ReadAllText(file);
            var agente = ParseArquivo(nome, conteudo);
            _agentes[agente.Nome] = agente;
        }

        _logger.LogInformation("Agentes carregados: {Count} ({Names})",
            _agentes.Count, string.Join(", ", _agentes.Keys));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public AgenteDefinicao Obter(string nome)
    {
        if (!_agentes.TryGetValue(nome, out var agente))
            throw new InvalidOperationException($"Agente '{nome}' nao encontrado no catalogo. Disponiveis: {string.Join(", ", _agentes.Keys)}");

        return agente;
    }

    private string ResolvePath()
    {
        if (!string.IsNullOrEmpty(_customPath))
            return _customPath;

        var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "agentes");
        if (Directory.Exists(devPath))
            return devPath;

        return Path.Combine(AppContext.BaseDirectory, "Assets", "agentes");
    }

    private static AgenteDefinicao ParseArquivo(string nome, string conteudo)
    {
        var linhas = conteudo.Split('\n');
        var frontmatterEnd = -1;
        var frontmatterStart = -1;

        for (var i = 0; i < linhas.Length; i++)
        {
            var trimmed = linhas[i].Trim();
            if (trimmed == "---")
            {
                if (frontmatterStart < 0)
                    frontmatterStart = i;
                else
                {
                    frontmatterEnd = i;
                    break;
                }
            }
        }

        if (frontmatterStart < 0 || frontmatterEnd < 0)
            throw new InvalidOperationException($"Agente '{nome}.md': frontmatter ausente (esperado '---' delimitando campos)");

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = frontmatterStart + 1; i < frontmatterEnd; i++)
        {
            var linha = linhas[i].Trim();
            if (string.IsNullOrEmpty(linha)) continue;

            var colonIdx = linha.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = linha[..colonIdx].Trim();
            var value = linha[(colonIdx + 1)..].Trim();
            metadata[key] = value;
        }

        if (!metadata.TryGetValue("modelo", out var modelo) || string.IsNullOrEmpty(modelo))
            throw new InvalidOperationException($"Agente '{nome}.md': campo 'modelo' obrigatorio no frontmatter");

        if (!metadata.TryGetValue("temperatura", out var tempStr) || !double.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperatura))
            throw new InvalidOperationException($"Agente '{nome}.md': campo 'temperatura' invalido ou ausente");

        if (temperatura < 0 || temperatura > 2)
            throw new InvalidOperationException($"Agente '{nome}.md': temperatura deve estar entre 0 e 2, obtido {temperatura}");

        if (!metadata.TryGetValue("max_tokens", out var tokensStr) || !int.TryParse(tokensStr, out var maxTokens) || maxTokens <= 0)
            throw new InvalidOperationException($"Agente '{nome}.md': campo 'max_tokens' invalido ou ausente");

        var body = string.Join("\n", linhas.Skip(frontmatterEnd + 1)).Trim();
        if (string.IsNullOrEmpty(body))
            throw new InvalidOperationException($"Agente '{nome}.md': persona (corpo do arquivo) esta vazia");

        return new AgenteDefinicao(nome.ToLowerInvariant(), modelo, temperatura, maxTokens, body);
    }
}
