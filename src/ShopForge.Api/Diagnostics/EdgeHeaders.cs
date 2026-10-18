using Microsoft.AspNetCore.HttpOverrides;

namespace ShopForge.Api.Diagnostics;

// Behind Front Door the original host and scheme arrive as headers, and a header is only worth reading when it comes
// from the edge: the profile id proves that (D-075). A request without it is refused, not merely stripped (D-207):
// Container Apps filters its ingress by address range only, so this profile's id is the lock that keeps anybody from
// reaching the app around the edge. The health checks stay open, because the platform probes the container directly.
internal static class EdgeHeaders
{
    private const string ProfileIdHeader = "X-Azure-FDID";

    public static IApplicationBuilder UseEdgeHeaders(this WebApplication app)
    {
        var profileId = app.Configuration["Edge:FrontDoorId"];

        if (app.Environment.IsDevelopment() || string.IsNullOrWhiteSpace(profileId))
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Headers[ProfileIdHeader], profileId, StringComparison.OrdinalIgnoreCase))
            {
                if (!context.Request.Path.StartsWithSegments("/health"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;

                    return;
                }

                context.Request.Headers.Remove("X-Forwarded-Host");
                context.Request.Headers.Remove("X-Forwarded-Proto");
                context.Request.Headers.Remove("X-Forwarded-For");
            }

            await next(context);
        });

        app.UseForwardedHeaders();

        return app;
    }

    public static IServiceCollection AddEdgeHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;

            // Front Door is the only hop, and it is recognised by its profile id rather than by an address range.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
