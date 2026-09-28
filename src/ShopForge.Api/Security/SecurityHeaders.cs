namespace ShopForge.Api.Security;

// What a browser should refuse to do with anything this API returns. The API answers JSON and files, never a
// page, so the strictest policy is also the correct one: nothing it sends should ever be executed, framed or
// sniffed into something else. The headers the storefront and admin pages need are the edge's to set, because
// the pages are served from there and not from here (D-126).
internal static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

            // Promising a year of HTTPS over a plain connection would be a promise to the wrong party, and in
            // development there is no HTTPS to promise.
            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            await next();
        });
}
