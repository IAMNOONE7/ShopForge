using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ShopForge.Shared.Diagnostics;

namespace ShopForge.Infrastructure.Diagnostics;

internal static class Observability
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMetrics();
        services.AddSingleton<ShopForgeMetrics>();
        services.AddSingleton<IShopForgeMetrics>(provider => provider.GetRequiredService<ShopForgeMetrics>());
        services.AddSingleton<OutboxGauges>();
        services.AddHostedService(provider => provider.GetRequiredService<OutboxGauges>());

        var otlpEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"];

        // Without a collector to send to there is nothing to export; the meters and activities still exist, so a
        // local `dotnet-counters` or a test listener sees them (D-069).
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("shopforge-api"))
            .WithTracing(tracing => tracing
                .AddSource(ShopForgeMetrics.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(ShopForgeMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            telemetry.UseOtlpExporter();
        }

        return services;
    }
}
