using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.IA;

[ExcludeFromCodeCoverage]
public record ChatMessage(string Role, string Content);
