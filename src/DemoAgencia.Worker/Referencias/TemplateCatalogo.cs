using System.Text.Json;

namespace DemoAgencia.Worker.Referencias;

public class TemplateCatalogo : IHostedService, ITemplateCatalogo
{
    private readonly ILogger<TemplateCatalogo> _logger;
    private readonly string? _customPath;
    private readonly Dictionary<string, TemplateDefinicao> _templates = new(StringComparer.OrdinalIgnoreCase);

    public TemplateCatalogo(ILogger<TemplateCatalogo> logger, string? customPath = null)
    {
        _logger = logger;
        _customPath = customPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();

        if (!Directory.Exists(path))
        {
            _logger.LogWarning("Diretorio de templates nao encontrado: {Path}", path);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Carregando templates de: {Path}", path);

        var htmlFiles = Directory.GetFiles(path, "*.html")
            .Where(f =>
            {
                var nome = Path.GetFileNameWithoutExtension(f);
                return nome.Contains('_');
            })
            .ToList();

        foreach (var htmlFile in htmlFiles)
        {
            try
            {
                var id = Path.GetFileNameWithoutExtension(htmlFile);
                var content = File.ReadAllText(htmlFile);
                var metadata = CarregarSidecar(id, path);

                _templates[id] = new TemplateDefinicao(
                    id,
                    content,
                    metadata?.Cliente,
                    metadata?.Fase,
                    metadata?.SubJornadas ?? new List<string>(),
                    metadata?.PalavrasChave ?? new List<string>(),
                    metadata?.Padrao ?? false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar template {File}", htmlFile);
            }
        }

        _logger.LogInformation("Templates carregados: {Count}", _templates.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public string? Obter(string id)
    {
        return _templates.TryGetValue(id, out var t) ? t.Content : null;
    }

    public string? Default
    {
        get
        {
            var padrao = _templates.Values.FirstOrDefault(t => t.Padrao);
            if (padrao != null) return padrao.Content;

            var primeiro = _templates.Values
                .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return primeiro?.Content;
        }
    }

    public string Selecionar(string? cliente, string? fase, string? subJornada, string? texto)
    {
        if (_templates.Count == 0)
            return string.Empty;

        var candidatos = _templates.Values.ToList();

        if (!string.IsNullOrEmpty(cliente))
        {
            var comCliente = candidatos.Where(t =>
                string.IsNullOrEmpty(t.Cliente) ||
                t.Cliente.Equals(cliente, StringComparison.OrdinalIgnoreCase)).ToList();
            if (comCliente.Count > 0)
                candidatos = comCliente;
            else
                candidatos = candidatos.Where(t => t.Padrao || string.IsNullOrEmpty(t.Cliente)).ToList();
        }

        var melhor = candidatos
            .Select(t => (template: t, score: CalcularScore(t, fase, subJornada, texto)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.template.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (melhor.score > 0)
            return melhor.template.Id;

        var defaultId = candidatos
            .Where(t => t.Padrao)
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Id;

        if (defaultId != null)
            return defaultId;

        return _templates.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).First();
    }

    private static int CalcularScore(TemplateDefinicao t, string? fase, string? subJornada, string? texto)
    {
        var score = 0;

        if (!string.IsNullOrEmpty(fase) && !string.IsNullOrEmpty(t.Fase))
        {
            if (t.Fase.Equals(fase, StringComparison.OrdinalIgnoreCase))
                score += 10;
        }

        if (!string.IsNullOrEmpty(subJornada) && t.SubJornadas.Count > 0)
        {
            if (t.SubJornadas.Any(s => s.Equals(subJornada, StringComparison.OrdinalIgnoreCase)))
                score += 5;
        }

        if (!string.IsNullOrEmpty(texto) && t.PalavrasChave.Count > 0)
        {
            var textoLower = texto.ToLowerInvariant();
            score += t.PalavrasChave.Count(p => textoLower.Contains(p.ToLowerInvariant()));
        }

        return score;
    }

    private TemplateMetadata? CarregarSidecar(string id, string path)
    {
        var jsonFile = Path.Combine(path, id + ".json");
        if (!File.Exists(jsonFile))
            return null;

        try
        {
            var json = File.ReadAllText(jsonFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new TemplateMetadata
            {
                Cliente = root.TryGetProperty("cliente", out var c) ? c.GetString() : null,
                Fase = root.TryGetProperty("fase", out var f) ? f.GetString() : null,
                SubJornadas = ExtrairLista(root, "sub_jornadas"),
                PalavrasChave = ExtrairLista(root, "palavras_chave"),
                Padrao = root.TryGetProperty("padrao", out var p) && p.GetBoolean()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar sidecar {File}", jsonFile);
            return null;
        }
    }

    private static List<string> ExtrairLista(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var lista = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrEmpty(s)) lista.Add(s);
        }
        return lista;
    }

    private string ResolvePath()
    {
        if (!string.IsNullOrEmpty(_customPath))
            return _customPath;

        var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "referencias", "templates");
        if (Directory.Exists(devPath))
            return devPath;

        return Path.Combine(AppContext.BaseDirectory, "Assets", "referencias", "templates");
    }

    private record TemplateDefinicao(
        string Id,
        string Content,
        string? Cliente,
        string? Fase,
        List<string> SubJornadas,
        List<string> PalavrasChave,
        bool Padrao);

    private class TemplateMetadata
    {
        public string? Cliente { get; init; }
        public string? Fase { get; init; }
        public List<string> SubJornadas { get; init; } = new();
        public List<string> PalavrasChave { get; init; } = new();
        public bool Padrao { get; init; }
    }
}
