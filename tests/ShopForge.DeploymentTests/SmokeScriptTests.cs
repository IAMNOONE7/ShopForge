using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ShopForge.DeploymentTests;

// The script that decides whether a release worked, run against a stand-in for what it checks: a site published the
// way the edge should serve it, over HTTPS as it is served, and the same site with one thing wrong at a time. A check
// that cannot fail is not a check (D-205).
public sealed class SmokeScriptTests : IDisposable
{
    private const string Released = "0123456789abcdef0123456789abcdef01234567";

    // The stand-in's certificate, which curl is told to trust for these runs and no others.
    private readonly SelfSigned _certificate = new();

    public void Dispose() => _certificate.Dispose();

    [Fact]
    public async Task A_site_published_as_it_should_be_passes()
    {
        await using var site = await FakeSite.StartAsync(new SiteShape(), _certificate);

        var smoke = await SiteSmokeAsync(site);

        Assert.True(smoke.ExitCode == 0, smoke.Output);
        Assert.Contains("All smoke checks passed.", smoke.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", smoke.Output, StringComparison.Ordinal);
        Assert.Contains("ok       header Strict-Transport-Security", smoke.Output, StringComparison.Ordinal);
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
    [InlineData("origin answers anybody", "a request around the edge is refused", 1)]
    public async Task A_site_with_one_thing_wrong_fails_and_says_which(string wrong, string check, int failures)
    {
        await using var site = await FakeSite.StartAsync(SiteShape.With(wrong), _certificate);

        var smoke = await SiteSmokeAsync(site);

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains($"FAILED   {check}", smoke.Output, StringComparison.Ordinal);
        Assert.Contains($"{failures} smoke check(s) failed.", smoke.Output, StringComparison.Ordinal);
    }

    // A site on plain HTTP, as on this machine, cannot promise HTTPS, so it is not asked to; everything else still is.
    [Fact]
    public async Task A_plain_http_site_is_not_asked_to_promise_https()
    {
        await using var site = await FakeSite.StartAsync(SiteShape.With("no transport security"), _certificate);

        var smoke = await RunAsync("--site", $"{site.HttpOrigin}/", "--admin", $"{site.HttpOrigin}/admin/orders", "--store", $"{site.HttpOrigin}/");

        Assert.True(smoke.ExitCode == 0, smoke.Output);
        Assert.Contains("skipped  header Strict-Transport-Security (the site is plain HTTP)", smoke.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_checks_pass_against_the_released_build()
    {
        await using var api = await FakeSite.StartAsync(new SiteShape(), _certificate);

        var smoke = await RunAsync("--api", api.HttpOrigin, "--version", Released, "--store-host", "shop.example.test");

        Assert.True(smoke.ExitCode == 0, smoke.Output);
        Assert.Contains($"ok       the released build answers (1.0.0+{Released})", smoke.Output, StringComparison.Ordinal);
    }

    // The revision a release replaces passes every other check, which is exactly why this one exists.
    [Fact]
    public async Task The_api_checks_fail_against_the_build_being_replaced()
    {
        await using var api = await FakeSite.StartAsync(new SiteShape(), _certificate);

        var smoke = await RunAsync("--api", api.HttpOrigin, "--version", "fedcba9876543210fedcba9876543210fedcba98");

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains("FAILED   the released build answers", smoke.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_answering_is_a_failure_and_not_the_end_of_the_run()
    {
        var smoke = await RunAsync("--api", "http://127.0.0.1:1", "--version", Released);

        Assert.Equal(1, smoke.ExitCode);
        Assert.Contains("FAILED   health/live (expected 200, got 000)", smoke.Output, StringComparison.Ordinal);
        Assert.Contains("5 smoke check(s) failed.", smoke.Output, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Errors)> SiteSmokeAsync(FakeSite site) =>
        RunAsync(
            "--site", $"{site.HttpsOrigin}/",
            "--admin", $"{site.HttpsOrigin}/admin/orders",
            "--http", $"{site.HttpOrigin}/plain",
            "--store", $"{site.HttpsOrigin}/",
            "--origin", $"{site.HttpsOrigin}/origin");

    private Task<(int ExitCode, string Output, string Errors)> RunAsync(params string[] arguments) =>
        Repository.RunAsync("bash", ["scripts/smoke.sh", .. arguments], new Dictionary<string, string?> { ["CURL_CA_BUNDLE"] = _certificate.PemPath });

    // What the stand-in serves, everything right unless one thing is named wrong.
    private sealed record SiteShape(string? Wrong = null)
    {
        public static SiteShape With(string wrong) => new(wrong);

        public bool Is(string wrong) => Wrong == wrong;
    }

    // A certificate for 127.0.0.1 that exists for one test class, written where curl can be told to trust it.
    private sealed class SelfSigned : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("shopforge-smoke-");

        public SelfSigned()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            Certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), password: null);
            PemPath = Path.Combine(_directory.FullName, "stand-in.pem");
            File.WriteAllText(PemPath, created.ExportCertificatePem());
        }

        public X509Certificate2 Certificate { get; }

        public string PemPath { get; }

        public void Dispose()
        {
            Certificate.Dispose();
            _directory.Delete(recursive: true);
        }
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

        private FakeSite(WebApplication app)
        {
            _app = app;
            HttpOrigin = app.Urls.Single(url => url.StartsWith("http://", StringComparison.Ordinal));
            HttpsOrigin = app.Urls.Single(url => url.StartsWith("https://", StringComparison.Ordinal));
        }

        public string HttpOrigin { get; }

        public string HttpsOrigin { get; }

        public static async Task<FakeSite> StartAsync(SiteShape shape, SelfSigned certificate)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(IPAddress.Loopback, 0);
                kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate.Certificate));
            });
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

            // The app itself, reached around the edge: everything but its health is refused, unless told otherwise.
            app.MapGet("/origin/api/storefront/store", () => shape.Is("origin answers anybody") ? Results.Json(new { name = "Wooden Home" }) : Results.StatusCode(StatusCodes.Status403Forbidden));
            app.MapGet("/origin/health/live", () => Results.Text("Healthy"));

            await app.StartAsync(TestContext.Current.CancellationToken);

            return new FakeSite(app);
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
