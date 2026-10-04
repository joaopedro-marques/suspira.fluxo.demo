using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Configuracoes;

[ExcludeFromCodeCoverage]
public class TelegramOptions
{
    public const string Section = "Telegram";
    public string BotToken { get; set; } = "";
}
