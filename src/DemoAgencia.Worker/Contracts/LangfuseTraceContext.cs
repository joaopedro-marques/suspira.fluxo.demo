using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Contracts;

[ExcludeFromCodeCoverage]
public class LangfuseTraceContext
{
    public long ChatId { get; set; }
    public string Operacao { get; set; } = "";
    public string Modelo { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
