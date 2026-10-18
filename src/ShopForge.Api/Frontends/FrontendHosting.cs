using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace ShopForge.Api.Frontends;

// The two built frontends, served by the app itself rather than from storage behind the edge (D-206). Each has its own
// fallback: a storefront route gets the storefront's page, an /admin route the admin's. Nothing the API, its health or
// a missing asset answers ever turns into a page. The page is never cached, the hashed assets it names are cached for
// good, and each page carries a content security policy of its own. Off wherever the built files are not there, as in
// development, where Vite serves them.
internal static class FrontendHosting
{
    private const string AdminPath = "/admin";

    // Packeta's map is the one script from elsewhere, fetched only when a shopper opens it (30d). Its window is theirs,
    // and the styles it sets on the page are inline, which is the only reason inline styles are allowed here.
    private const string StorefrontPolicy =
        "default-src 'self'; script-src 'self' https://widget.packeta.com; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; font-src 'self' data:; connect-src 'self' https://widget.packeta.com; "
        + "frame-src https://widget.packeta.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string AdminPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; "
        + "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // Packeta's map is told which shop opened it, and nothing about the page it was opened on. The admin tells nobody.
    private const string StorefrontReferrers = "strict-origin-when-cross-origin";

    private const string AdminReferrers = "no-referrer";

    public static WebApplication UseFrontends(this WebApplication app)
    {
        var root = app.Configuration["Frontends:Root"] ?? Path.Combine(AppContext.BaseDirectory, "frontends");
        var admin = Directory.Exists(Path.Combine(root, "admin")) ? new PhysicalFileProvider(Path.Combine(root, "admin")) : null;
        var storefront = Directory.Exists(Path.Combine(root, "storefront")) ? new PhysicalFileProvider(Path.Combine(root, "storefront")) : null;

        // The admin belongs to the platform's own hosts, not to every shop's: no sign-in page on a merchant's domain, and
        // no session cookie set there. Those are the admin's domain and the edge's own host name, which needs no DNS of
        // the platform's, so the admin is there before its domain is and a release checks it there (D-208). A platform
        // host's root sends a visitor to the admin.
        string[] adminHosts =
        [
            .. new[] { app.Configuration["Frontends:AdminHost"], app.Configuration["Frontends:EdgeHost"] }
                .OfType<string>()
                .Where(host => !string.IsNullOrWhiteSpace(host)),
        ];

        if (adminHosts.Length > 0)
        {
            app.Use(async (context, next) =>
            {
                var onAdminHost = adminHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase);

                if (context.Request.Path.StartsWithSegments(AdminPath) && !onAdminHost)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;

                    return;
                }

                if (onAdminHost && context.Request.Path == "/" && HttpMethods.IsGet(context.Request.Method))
                {
                    context.Response.Redirect($"{AdminPath}/");

                    return;
                }

                await next(context);
            });
        }

        if (admin is not null)
        {
            app.UseStaticFiles(new StaticFileOptions { FileProvider = admin, RequestPath = AdminPath, OnPrepareResponse = Cached(AdminPolicy, AdminReferrers) });
            app.MapFallbackToFile($"{AdminPath}/{{*path:nonfile}}", "index.html", new StaticFileOptions { FileProvider = admin, OnPrepareResponse = Cached(AdminPolicy, AdminReferrers) });
        }

        if (storefront is not null)
        {
            app.UseStaticFiles(new StaticFileOptions { FileProvider = storefront, OnPrepareResponse = Cached(StorefrontPolicy, StorefrontReferrers) });
            app.MapFallbackToFile("{*path:nonfile}", "index.html", new StaticFileOptions { FileProvider = storefront, OnPrepareResponse = Cached(StorefrontPolicy, StorefrontReferrers) });
        }

        // An address under the API or the health checks that nothing answers is a 404, never a page.
        if (admin is not null || storefront is not null)
        {
            app.MapFallback("/api/{**rest}", () => Results.NotFound());
            app.MapFallback("/health/{**rest}", () => Results.NotFound());
        }

        return app;
    }

    // A page is asked for again every time, so a release is never served half-old; the hashed files it names change
    // name when they change, so they are kept for a year. Anything else at the root (an icon) for an hour.
    private static Action<StaticFileResponseContext> Cached(string policy, string referrers) => context =>
    {
        var headers = context.Context.Response.Headers;

        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "no-cache";
            headers.ContentSecurityPolicy = policy;
            headers["Referrer-Policy"] = referrers;
        }
        else if (context.Context.Request.Path.Value?.Contains("/assets/", StringComparison.Ordinal) == true)
        {
            headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else
        {
            headers.CacheControl = "public, max-age=3600";
        }
    };
}
