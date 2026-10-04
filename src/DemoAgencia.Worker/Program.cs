using System.Diagnostics.CodeAnalysis;
using DemoAgencia.Worker;
using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.Grafana.Loki;

[ExcludeFromCodeCoverage]
public static class Program
{
    public static void Main(string[] args)
    {
        SelfLog.Enable(Console.Error);

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration);

        var loki = builder.Configuration.GetSection("GrafanaLoki");
        var lokiEndpoint = loki["Endpoint"];
        if (Uri.TryCreate(lokiEndpoint, UriKind.Absolute, out var lokiUri)
            && (lokiUri.Scheme == Uri.UriSchemeHttp || lokiUri.Scheme == Uri.UriSchemeHttps))
        {
            loggerConfig = loggerConfig.WriteTo.GrafanaLoki(
                lokiUri.ToString(),
                credentials: new LokiCredentials { Login = loki["LoginId"] ?? string.Empty, Password = loki["Password"] ?? string.Empty },
                labels: [new LokiLabel { Key = "app", Value = "demoagencia" },
                         new LokiLabel { Key = "env", Value = "production" }]);
        }

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information("Iniciando DemoAgencia Worker...");

            builder.Logging.ClearProviders();
            builder.Services.AddDemoAgencia(builder.Configuration);

            var host = builder.Build();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Aplicacao encerrada inesperadamente");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
