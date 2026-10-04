using System.Diagnostics.CodeAnalysis;
using DemoAgencia.Worker;
using Serilog;
using Serilog.Debugging;

[ExcludeFromCodeCoverage]
public static class Program
{
    public static void Main(string[] args)
    {
        SelfLog.Enable(Console.Error);

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();

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
