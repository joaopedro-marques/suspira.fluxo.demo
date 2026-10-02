using DemoAgencia.Worker;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.Seguranca;
using Serilog;

try
{
    Log.Information("Iniciando DemoAgencia Worker...");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog(config => config
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/demo-log-.txt",
            rollingInterval: Serilog.RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddHttpClient("OpenRouter", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(120);
    }).AddHttpMessageHandler<OpenRouterPrivacyHandler>();

    builder.Services.AddTransient<OpenRouterPrivacyHandler>();

    builder.Services.AddHostedService<Worker>();
    builder.Services.AddSingleton<DemoAgencia.Worker.Agentes.AgenteLoader>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DemoAgencia.Worker.Agentes.AgenteLoader>());
    builder.Services.AddSingleton<DemoAgencia.Worker.Referencias.ReferenciaClienteLoader>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DemoAgencia.Worker.Referencias.ReferenciaClienteLoader>());
    builder.Services.AddSingleton<DemoAgencia.Worker.IA.HistoricoChat>();
    builder.Services.AddSingleton<DemoAgencia.Worker.Observabilidade.LangfuseClient>();
    builder.Services.AddSingleton<DemoAgencia.Worker.Observabilidade.LangfuseInterceptor>();
    builder.Services.AddSingleton<AnonimizadorService>();
    builder.Services.AddSingleton<RateLimiterService>();
    builder.Services.AddSingleton<DemoAgencia.Worker.IA.OpenRouterService>();
    builder.Services.AddSingleton<DemoAgencia.Worker.IA.PipelineService>();
    builder.Services.AddSingleton<DemoAgencia.Worker.IA.StreamingService>();
    builder.Services.AddSingleton<FerramentaRegistry>(sp =>
    {
        var registry = new FerramentaRegistry();
        registry.Registrar(new GerarImagemFerramenta(
            sp.GetRequiredService<ILogger<GerarImagemFerramenta>>(),
            sp.GetRequiredService<DemoAgencia.Worker.IA.OpenRouterService>()));
        return registry;
    });
    builder.Services.AddSingleton<DemoAgencia.Worker.IA.OrquestradorLoop.OrquestradorLoopService>();
    builder.Services.AddHostedService<DemoAgencia.Worker.Telegram.TelegramService>();

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
