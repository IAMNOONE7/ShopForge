using Microsoft.AspNetCore.HttpOverrides;

namespace ShopForge.Api.Diagnostics;

// Behind Front Door the original host and scheme arrive as headers, and a header is only worth reading when it comes
// from the edge: the profile id proves that (D-075). Anything else is ignored, so nobody reaches another store by
// sending `X-Forwarded-Host` at the container.
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
