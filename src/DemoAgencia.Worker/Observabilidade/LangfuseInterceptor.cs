using DemoAgencia.Worker.Contracts;
using DemoAgencia.Worker.Seguranca;

namespace DemoAgencia.Worker.Observabilidade;

public class LangfuseInterceptor
{
    private readonly ILogger<LangfuseInterceptor> _logger;
    private readonly LangfuseClient _langfuseClient;
    private readonly AnonimizadorService _anonimizador;

    public LangfuseInterceptor(
        ILogger<LangfuseInterceptor> logger,
        LangfuseClient langfuseClient,
        AnonimizadorService anonimizador)
    {
        _logger = logger;
        _langfuseClient = langfuseClient;
        _anonimizador = anonimizador;
    }

    public LangfuseTraceContext IniciarTrace(long chatId, string operacao, string modelo)
    {
        return new LangfuseTraceContext
        {
            ChatId = chatId,
            Operacao = operacao,
            Modelo = modelo,
            StartTime = DateTime.UtcNow
        };
    }

    public async Task FinalizarTraceAsync(
        LangfuseTraceContext contexto,
        string input,
        string output,
        CancellationToken ct = default)
    {
        contexto.EndTime = DateTime.UtcNow;

        var inputAnonimizado = _anonimizador.Anonimizar(input);
        var outputAnonimizado = _anonimizador.Anonimizar(output);

        var trace = new LangfuseTrace
        {
            Name = contexto.Operacao,
            UserId = contexto.ChatId.ToString(),
            Model = contexto.Modelo,
            Input = new { message = inputAnonimizado },
            Output = new { response = outputAnonimizado },
            StartTime = contexto.StartTime,
            EndTime = contexto.EndTime,
            Metadata = new Dictionary<string, object>
            {
                ["chatId"] = contexto.ChatId,
                ["duration_ms"] = (contexto.EndTime - contexto.StartTime).TotalMilliseconds
            },
            ObservationMetadata = new Dictionary<string, object>
            {
                ["source"] = "openrouter",
                ["type"] = contexto.Operacao
            },
            Tags = new[] { "telegram", contexto.Operacao }
        };

        await _langfuseClient.EnviarTraceAsync(trace, ct);
    }
}
