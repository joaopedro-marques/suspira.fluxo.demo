using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using DemoAgencia.Worker.Telegram;
using Microsoft.Extensions.Options;
using Serilog;

namespace DemoAgencia.Worker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDemoAgencia(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSerilog();

        services.Configure<LoopOptions>(configuration.GetSection(LoopOptions.Section));
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.Section));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.Section));
        services.Configure<SegurancaOptions>(configuration.GetSection(SegurancaOptions.Section));

        services.AddHttpClient("OpenRouter", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddHttpMessageHandler<OpenRouterPrivacyHandler>();

        services.AddTransient<OpenRouterPrivacyHandler>();

        services.AddSingleton<AgenteLoader>();
        services.AddSingleton<IAgentesCatalogo>(sp => sp.GetRequiredService<AgenteLoader>());
        services.AddHostedService(sp => sp.GetRequiredService<AgenteLoader>());

        services.AddSingleton<ReferenciaClienteLoader>();
        services.AddSingleton<IReferenciasCliente>(sp => sp.GetRequiredService<ReferenciaClienteLoader>());
        services.AddHostedService(sp => sp.GetRequiredService<ReferenciaClienteLoader>());

        services.AddSingleton<HistoricoChat>();
        services.AddSingleton<IHistoricoChat>(sp => sp.GetRequiredService<HistoricoChat>());

        services.AddSingleton<AnonimizadorService>();
        services.AddSingleton<LangfuseClient>();
        services.AddSingleton<LangfuseInterceptor>();
        services.AddSingleton<RateLimiterService>();

        services.AddSingleton<OpenRouterService>();
        services.AddSingleton<IServicoChat>(sp => sp.GetRequiredService<OpenRouterService>());
        services.AddSingleton<IGeradorImagem>(sp => sp.GetRequiredService<OpenRouterService>());
        services.AddSingleton<IStreamingChat>(sp => sp.GetRequiredService<OpenRouterService>());
        services.AddSingleton<IAnalisadorImagem>(sp => sp.GetRequiredService<OpenRouterService>());

        services.AddSingleton<StreamingService>();
        services.AddSingleton<IStreamingService>(sp => sp.GetRequiredService<StreamingService>());

        services.AddSingleton<FerramentaRegistry>(sp =>
        {
            var registry = new FerramentaRegistry();
            registry.Registrar(new GerarImagemFerramenta(
                sp.GetRequiredService<ILogger<GerarImagemFerramenta>>(),
                sp.GetRequiredService<IGeradorImagem>()));
            return registry;
        });

        services.AddSingleton<OrquestradorLoopService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<OrquestradorLoopService>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = sp.GetRequiredService<IOptions<LoopOptions>>();
            var openRouter = sp.GetRequiredService<IServicoChat>();
            var agentes = sp.GetRequiredService<IAgentesCatalogo>();
            var refs = sp.GetRequiredService<IReferenciasCliente>();
            var ferramentas = sp.GetRequiredService<FerramentaRegistry>();
            var analisador = sp.GetRequiredService<IAnalisadorImagem>();
            return new OrquestradorLoopService(logger, loggerFactory, options, openRouter, agentes, refs, ferramentas, analisador);
        });

        services.AddSingleton<ITelegramGatewayFactory, TelegramGatewayFactory>();
        services.AddHostedService<TelegramService>();

        return services;
    }
}
