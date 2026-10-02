using DemoAgencia.Worker;
using Serilog;

try
{
    Log.Information("Iniciando DemoAgencia Worker...");

    var builder = Host.CreateApplicationBuilder(args);
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
