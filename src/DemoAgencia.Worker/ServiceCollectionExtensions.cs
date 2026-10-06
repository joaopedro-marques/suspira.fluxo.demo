using System.Diagnostics.CodeAnalysis;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
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

        services.AddSingleton<ReferenciaClienteLoader>();
        services.AddSingleton<IReferenciasCliente>(sp => sp.GetRequiredService<ReferenciaClienteLoader>());
        services.AddHostedService(sp => sp.GetRequiredService<ReferenciaClienteLoader>());

        services.AddSingleton<AnonimizadorService>();
        services.AddSingleton<LangfuseClient>();
        services.AddSingleton<LangfuseInterceptor>();
        services.AddSingleton<RateLimiterService>();

        services.AddSingleton<OpenRouterService>();
        services.AddSingleton<IServicoChat>(sp => sp.GetRequiredService<OpenRouterService>());
        services.AddSingleton<IGeradorImagem>(sp => sp.GetRequiredService<OpenRouterService>());
        services.AddSingleton<IAnalisadorImagem>(sp => sp.GetRequiredService<OpenRouterService>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ConversaPendenteStore>();

        services.AddSingleton<AgentesLoader>();
        services.AddSingleton<IAgentesCatalogo>(sp => sp.GetRequiredService<AgentesLoader>());
        services.AddHostedService(sp => sp.GetRequiredService<AgentesLoader>());

        services.AddSingleton<TemplateCatalogo>();
        services.AddSingleton<ITemplateCatalogo>(sp => sp.GetRequiredService<TemplateCatalogo>());
        services.AddHostedService(sp => sp.GetRequiredService<TemplateCatalogo>());

        services.AddSingleton<RouterService>();
        services.AddSingleton<PipelineRunner>();

        var heroSectionTemplate = """
        <tr>
          <td style="padding: 0; text-align: center;">
            <img src="{{hero_src}}" alt="Hero" style="display: block; max-width: 600px; width: 100%; height: auto; border: 0;" border="0" width="600">
          </td>
        </tr>
        """;

        services.AddSingleton(sp => new PipelineEmail(
            sp.GetRequiredService<IServicoChat>(),
            sp.GetRequiredService<IGeradorImagem>(),
            sp.GetRequiredService<IReferenciasCliente>(),
            sp.GetRequiredService<IAnalisadorImagem>(),
            sp.GetRequiredService<IAgentesCatalogo>(),
            sp.GetRequiredService<ITemplateCatalogo>(),
            heroSectionTemplate));

        services.AddSingleton<ITelegramGatewayFactory, TelegramGatewayFactory>();
        services.AddHostedService<TelegramService>();

        return services;
    }
}
