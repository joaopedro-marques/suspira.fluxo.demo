using System.Diagnostics.CodeAnalysis;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.IA.PreFlight;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using DemoAgencia.Worker.Telegram;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Serilog;

namespace DemoAgencia.Worker;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDemoAgencia(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSerilog();

        services.Configure<LoopOptions>(configuration.GetSection(LoopOptions.Section));
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.Section));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.Section));
        services.Configure<SegurancaOptions>(configuration.GetSection(SegurancaOptions.Section));
        services.Configure<PreFlightOptions>(configuration.GetSection(PreFlightOptions.Section));

        var openRouterConfig = configuration.GetSection(OpenRouterOptions.Section).Get<OpenRouterOptions>() ?? new OpenRouterOptions();

        services.AddHttpClient("OpenRouter", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        })
        .AddHttpMessageHandler<OpenRouterPrivacyHandler>()
        .AddHttpMessageHandler<ReasoningDisablingHandler>()
        .AddResilienceHandler("openrouter-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = openRouterConfig.MaxRetriesHttp,
                Delay = TimeSpan.FromSeconds(openRouterConfig.BackoffBaseSegundos),
                MaxDelay = TimeSpan.FromSeconds(openRouterConfig.BackoffMaxSegundos),
            });
        });

        services.AddTransient<OpenRouterPrivacyHandler>();
        services.AddTransient<ReasoningDisablingHandler>();

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

        services.AddSingleton<EnriquecedorContextoCliente>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ConversaPendenteStore>();
        services.AddSingleton<RouterService>();

        services.AddSingleton<StreamingService>();
        services.AddSingleton<IStreamingService>(sp => sp.GetRequiredService<StreamingService>());

        services.AddSingleton<FerramentaRegistry>(sp =>
        {
            var registry = new FerramentaRegistry();
            var loopOptions = sp.GetRequiredService<IOptions<LoopOptions>>().Value;
            registry.Registrar(new GerarImagemFerramenta(
                sp.GetRequiredService<ILogger<GerarImagemFerramenta>>(),
                sp.GetRequiredService<IGeradorImagem>(),
                sp.GetRequiredService<IReferenciasCliente>(),
                sp.GetRequiredService<IAnalisadorImagem>(),
                loopOptions));
            registry.Registrar(new PlanejarDeckFerramenta(
                sp.GetRequiredService<ILogger<PlanejarDeckFerramenta>>()));
            registry.Registrar(new ListarAssetsFerramenta(
                sp.GetRequiredService<ILogger<ListarAssetsFerramenta>>(),
                sp.GetRequiredService<IReferenciasCliente>()));
            registry.Registrar(new AnexarAssetFerramenta(
                sp.GetRequiredService<ILogger<AnexarAssetFerramenta>>(),
                sp.GetRequiredService<IReferenciasCliente>()));
            return registry;
        });

        services.AddSingleton<OrquestradorLoopService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<OrquestradorLoopService>>();
            var options = sp.GetRequiredService<IOptions<LoopOptions>>();
            var openRouter = sp.GetRequiredService<IServicoChat>();
            var agentes = sp.GetRequiredService<IAgentesCatalogo>();
            var refs = sp.GetRequiredService<IReferenciasCliente>();
            var ferramentas = sp.GetRequiredService<FerramentaRegistry>();
            var enriquecedor = sp.GetRequiredService<EnriquecedorContextoCliente>();
            return new OrquestradorLoopService(logger, options, openRouter, agentes, refs, ferramentas, enriquecedor);
        });

        services.AddSingleton<ITelegramGatewayFactory, TelegramGatewayFactory>();
        services.AddHostedService<TelegramService>();

        return services;
    }
}
