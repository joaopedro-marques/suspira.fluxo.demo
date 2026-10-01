using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA;

public class RoteadorService
{
    private readonly ILogger<RoteadorService> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;

    private readonly Dictionary<string, string> _categoriaParaModelo = new()
    {
        ["codigo"] = "anthropic/claude-3.5-sonnet",
        ["estrategia"] = "meta-llama/llama-3.1-70b-instruct",
        ["copy"] = "anthropic/claude-3.5-sonnet",
        ["geral"] = "google/gemini-flash-1.5"
    };

    public RoteadorService(
        ILogger<RoteadorService> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
    }

    public async Task<(string Modelo, string? Persona)> RoteearAsync(
        long chatId,
        string mensagem,
        string? comando = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(comando))
        {
            var agente = _agenteLoader.ObterPorComando(comando);
            if (agente != null)
            {
                _logger.LogInformation("Roteamento por comando {Comando} -> {Modelo}", comando, agente.ModeloAlvo);
                return (agente.ModeloAlvo, agente.Persona);
            }
        }

        var categoria = await _openRouter.ClassificarAsync(mensagem, ct);
        var modelo = _categoriaParaModelo.TryGetValue(categoria, out var m) ? m : _categoriaParaModelo["geral"];

        AgenteDefinicao? agenteCategoria = null;
        switch (categoria)
        {
            case "codigo":
                agenteCategoria = _agenteLoader.ListarAgentes().FirstOrDefault(a => a.Nome == "Dev");
                break;
            case "estrategia":
                agenteCategoria = _agenteLoader.ListarAgentes().FirstOrDefault(a => a.Nome == "Estrategista");
                break;
            case "copy":
                agenteCategoria = _agenteLoader.ListarAgentes().FirstOrDefault(a => a.Nome == "Redator");
                break;
        }

        _logger.LogInformation("Roteamento automático: {Categoria} -> {Modelo}", categoria, modelo);
        return (modelo, agenteCategoria?.Persona);
    }
}
