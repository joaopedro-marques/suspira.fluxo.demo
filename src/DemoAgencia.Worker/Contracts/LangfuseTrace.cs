using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Contracts;

[ExcludeFromCodeCoverage]
public class LangfuseTrace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "telegram-message";
    public string? UserId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string[]? Tags { get; set; }

    public string ObservationId { get; set; } = Guid.NewGuid().ToString();
    public string ObservationName { get; set; } = "chat-completion";
    public string? Model { get; set; }
    public object? Input { get; set; }
    public object? Output { get; set; }
    public Dictionary<string, object>? ObservationMetadata { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime EndTime { get; set; } = DateTime.UtcNow;
}
