namespace DemoAgencia.Worker.Observabilidade;

public class LangfuseInterceptor
{
    private readonly ILogger<LangfuseInterceptor> _logger;
    private readonly LangfuseClient _langfuseClient;

    public LangfuseInterceptor(ILogger<LangfuseInterceptor> logger, LangfuseClient langfuseClient)
    {
        _logger = logger;
        _langfuseClient = langfuseClient;
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

        var trace = new LangfuseTrace
        {
            Name = contexto.Operacao,
            UserId = contexto.ChatId.ToString(),
            Model = contexto.Modelo,
            Input = new { message = input },
            Output = new { response = output },
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

public class LangfuseTraceContext
{
    public long ChatId { get; set; }
    public string Operacao { get; set; } = "";
    public string Modelo { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
