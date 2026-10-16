using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ShopForge.DeploymentTests;

// The script that decides whether a release worked, run against a stand-in for what it checks: a site published the
// way the edge should serve it, and the same site with one thing wrong at a time. A check that cannot fail is not a
// check (D-205).
public sealed class SmokeScriptTests
{
    private const string Released = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task A_site_published_as_it_should_be_passes()
    {
        await using var site = await FakeSite.StartAsync(new SiteShape());

        var smoke = await SiteSmokeAsync(site);

        Assert.True(smoke.ExitCode == 0, smoke.Output);
        Assert.Contains("All smoke checks passed.", smoke.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", smoke.Output, StringComparison.Ordinal);
    }

    // A missing page names no bundle, and is the store's page too in this stand-in, so it is three failures.
    [Theory]
    [InlineData("bundle as page", "the storefront's bundle", 1)]
    [InlineData("admin falls back to the storefront", "an admin deep link", 1)]
    [InlineData("no content security policy", "header Content-Security-Policy is missing", 1)]
    [InlineData("no transport security", "header Strict-Transport-Security is missing", 1)]
    [InlineData("sniffing allowed", "header X-Content-Type-Options is not nosniff", 1)]
    [InlineData("framing allowed", "framing is refused", 1)]
    [InlineData("plain http served", "plain HTTP is sent on to HTTPS", 1)]
    [InlineData("redirect to plain http", "plain HTTP is sent on to HTTPS", 1)]
    [InlineData("store data missing", "the store's data behind its page", 1)]
    [InlineData("page missing", "the storefront's page", 3)]
    public async Task A_site_with_one_thing_wrong_fails_and_says_which(string wrong, string check, int failures)
    {
        await using var site = await FakeSite.StartAsync(SiteShape.With(wrong));

        var smoke = await SiteSmokeAsync(site);

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains($"FAILED   {check}", smoke.Output, StringComparison.Ordinal);
        Assert.Contains($"{failures} smoke check(s) failed.", smoke.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_checks_pass_against_the_released_build()
    {
        await using var api = await FakeSite.StartAsync(new SiteShape());

        var smoke = await Repository.RunAsync("bash", ["scripts/smoke.sh", "--api", api.Origin, "--version", Released, "--store-host", "shop.example.test"]);

        Assert.True(smoke.ExitCode == 0, smoke.Output);
        Assert.Contains($"ok       the released build answers (1.0.0+{Released})", smoke.Output, StringComparison.Ordinal);
    }

    // The revision a release replaces passes every other check, which is exactly why this one exists.
    [Fact]
    public async Task The_api_checks_fail_against_the_build_being_replaced()
    {
        await using var api = await FakeSite.StartAsync(new SiteShape());

        var smoke = await Repository.RunAsync("bash", ["scripts/smoke.sh", "--api", api.Origin, "--version", "fedcba9876543210fedcba9876543210fedcba98"]);

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains("FAILED   the released build answers", smoke.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_answering_is_a_failure_and_not_the_end_of_the_run()
    {
        var smoke = await Repository.RunAsync("bash", ["scripts/smoke.sh", "--api", "http://127.0.0.1:1", "--version", Released]);

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains("FAILED   health/live (expected 200, got 000)", smoke.Output, StringComparison.Ordinal);
        Assert.Contains("5 smoke check(s) failed.", smoke.Output, StringComparison.Ordinal);
    }

    private static Task<(int ExitCode, string Output, string Errors)> SiteSmokeAsync(FakeSite site) =>
        Repository.RunAsync("bash",
        [
            "scripts/smoke.sh",
            "--site", $"{site.Origin}/",
            "--admin", $"{site.Origin}/admin/orders",
            "--http", $"{site.Origin}/plain",
            "--store", $"{site.Origin}/",
        ]);

    // What the stand-in serves, everything right unless one thing is named wrong.
    private sealed record SiteShape(string? Wrong = null)
    {
        public static SiteShape With(string wrong) => new(wrong);

        public bool Is(string wrong) => Wrong == wrong;
    }

    private sealed class FakeSite : IAsyncDisposable
    {
        private const string StorefrontPage =
            "<!doctype html><html><head><script type=\"module\" crossorigin src=\"/assets/index-B1x2.js\"></script></head>"
            + "<body><div id=\"root\"></div></body></html>";

        private const string AdminPage =
            "<!doctype html><html><head><script type=\"module\" crossorigin src=\"/admin/assets/index-C3y4.js\"></script></head>"
            + "<body><div id=\"root\"></div></body></html>";

        private readonly WebApplication _app;

        private FakeSite(WebApplication app, string origin)
        {
            _app = app;
            Origin = origin;
        }

        public string Origin { get; }

        public static async Task<FakeSite> StartAsync(SiteShape shape)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                if (!shape.Is("no transport security"))
                {
                    context.Response.Headers.StrictTransportSecurity = "max-age=31536000";
                }

                // Without a policy, framing is still refused the older way, so only the policy is missing.
                if (shape.Is("no content security policy"))
                {
                    context.Response.Headers.XFrameOptions = "DENY";
                }
                else
                {
                    context.Response.Headers.ContentSecurityPolicy = shape.Is("framing allowed")
                        ? "default-src 'self'"
                        : "default-src 'self'; frame-ancestors 'none'";
                }

                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers.XContentTypeOptions = shape.Is("sniffing allowed") ? "none" : "nosniff";

                await next(context);
            });

            app.MapGet("/", () => shape.Is("page missing") ? Results.NotFound() : Results.Content(StorefrontPage, "text/html"));
            app.MapGet("/assets/{file}", (string file) => shape.Is("bundle as page")
                ? Results.Content(StorefrontPage, "text/html")
                : Results.Content("console.log('shop');", "text/javascript"));
            app.MapGet("/admin/{**path}", () => Results.Content(shape.Is("admin falls back to the storefront") ? StorefrontPage : AdminPage, "text/html"));
            app.MapGet("/plain", () => shape.Is("plain http served")
                ? Results.Content(StorefrontPage, "text/html")
                : Results.Redirect(shape.Is("redirect to plain http") ? "http://www.example.test/" : "https://www.example.test/", permanent: true, preserveMethod: true));
            app.MapGet("/api/storefront/store", (HttpContext context) =>
                context.Request.Headers.Host == "not-a-store.invalid" || shape.Is("store data missing")
                    ? Results.NotFound()
                    : Results.Json(new { name = "Wooden Home" }));
            app.MapGet("/health/live", () => Results.Text("Healthy"));
            app.MapGet("/health/ready", () => Results.Text("Healthy"));
            app.MapGet("/health/version", () => Results.Text($"1.0.0+{Released}"));
            app.MapPost("/api/admin/auth/login", () => Results.Unauthorized());

            await app.StartAsync(TestContext.Current.CancellationToken);

            return new FakeSite(app, app.Urls.Single());
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
