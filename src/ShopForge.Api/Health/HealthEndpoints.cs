using Microsoft.AspNetCore.HttpLogging;

namespace ShopForge.Api.Health;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live")
            .WithHttpLogging(HttpLoggingFields.None);
    }
}
