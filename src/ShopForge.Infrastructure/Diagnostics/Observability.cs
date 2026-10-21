using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        // Traces say what happened and metrics say how much; a log line is what says why, and until now it was
        // the one signal that never left the machine (D-125). The exporter below carries all three.
        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            // A log line is worth little without the message it was written with, and the trace it belongs to is
            // what ties it to the request that caused it (D-072).
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
        }));

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

        // In Azure the same instrumentation goes to Application Insights, logs included (D-077).
        if (!string.IsNullOrWhiteSpace(configuration["ApplicationInsights:ConnectionString"]))
        {
            telemetry.UseAzureMonitor(options => options.ConnectionString = configuration["ApplicationInsights:ConnectionString"]);
        }

        return services;
    }
}
