using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShopForge.IntegrationTests.Edge;

// Azure Resource Manager's Front Door calls, over HTTP, answered in the shapes Microsoft's REST reference gives for
// 2025-04-15 (D-212): custom domains, their validation token and its refresh, routes and security policies, with ids in
// the lower case Resource Manager likes to hand back. A domain is approved once its token is in public DNS; its
// certificate is issued a while after it is approved and on a route; both on the test's clock. It refuses what Front
// Door would: a second route on the endpoint's own host, a route or policy naming no domain or one it does not have, and
// a domain deleted while a route or a policy still lists it. A test can ask for a revalidation, make the next answer a
// 429, or make the next request hang.
internal sealed class RecordedFrontDoor : IAsyncDisposable
{
    public const string Token = "arm-token-made-up";
    public const string Subscription = "00000000-0000-0000-0000-000000000016";
    public const string Group = "shopforge-test-rg";
    public const string Profile = "shopforge-test-fd";
    public const string Endpoint = "shopforge-test-endpoint";
    public const string EndpointHost = "shopforge-test-endpoint-a1b2c3.z01.azurefd.net";
    public const string ApiVersion = "2025-04-15";

    private static readonly TimeSpan TokenLasts = TimeSpan.FromDays(7);

    private readonly WebApplication _app;
    private readonly TimeProvider _clock;
    private readonly Func<string, IReadOnlyList<string>> _publicTxt;
    private readonly ConcurrentDictionary<string, FakeEdgeDomain> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, JsonObject> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, JsonObject> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _elsewhere = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private int? _throttle;
    private bool _hang;
    private bool _forbid;

    private RecordedFrontDoor(WebApplication app, TimeProvider clock, Func<string, IReadOnlyList<string>> publicTxt)
    {
        _app = app;
        _clock = clock;
        _publicTxt = publicTxt;
    }

    public static string ProfileId => $"/subscriptions/{Subscription}/resourceGroups/{Group}/providers/Microsoft.Cdn/profiles/{Profile}";

    public static string WafPolicyId =>
        $"/subscriptions/{Subscription}/resourceGroups/{Group}/providers/Microsoft.Network/frontdoorWebApplicationFirewallPolicies/shopforgetestwaf";

    public string BaseUrl { get; private set; } = string.Empty;

    // How long after a domain is approved and on a route its certificate is issued.
    public TimeSpan CertificateTakes { get; set; } = TimeSpan.FromMinutes(30);

    // How long a read of a route takes, which a test lengthens to make two writers cross.
    public TimeSpan RouteReadTakes { get; set; } = TimeSpan.Zero;

    // Every write to a route or a policy, as "METHOD route|policy name".
    public ConcurrentQueue<string> Writes { get; } = new();

    public IReadOnlyCollection<FakeEdgeDomain> Domains => [.. _domains.Values];

    public static async Task<RecordedFrontDoor> StartAsync(TimeProvider clock, Func<string, IReadOnlyList<string>> publicTxt)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var frontDoor = new RecordedFrontDoor(app, clock, publicTxt);
        frontDoor.Map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        frontDoor.BaseUrl = $"{app.Urls.Single()}/";

        return frontDoor;
    }

    public FakeEdgeDomain? DomainFor(string hostName) => _domains.Values.SingleOrDefault(domain => domain.HostName == hostName);

    // A domain of the template's, such as the admin's or the stores' wildcard, as a deployment leaves it.
    public FakeEdgeDomain AddPlatformDomain(string name, string hostName)
    {
        var domain = new FakeEdgeDomain(name, hostName) { Token = $"token-{Guid.NewGuid():N}", TokenExpires = _clock.GetUtcNow() + TokenLasts };
        _domains[name] = domain;

        return domain;
    }

    // Forty-five days before its certificate runs out, a domain Front Door cannot renew by itself wants proving again;
    // its old token has long run out.
    public void RequireRevalidation(string hostName)
    {
        var domain = DomainFor(hostName) ?? _domains.Values.Single(candidate => candidate.HostName == hostName);
        domain.Validation = "PendingRevalidation";
        domain.TokenExpires = _clock.GetUtcNow() - TimeSpan.FromDays(100);
    }

    // A host another Front Door profile already has.
    public void TakenElsewhere(string hostName) => _elsewhere[hostName] = true;

    public IReadOnlyList<string> RouteDomains(string route) =>
        _routes.TryGetValue(route, out var found) ? Names(found["properties"]?["customDomains"]) : [];

    public IReadOnlyList<string> PolicyDomains(string policy) =>
        _policies.TryGetValue(policy, out var found)
            ? [.. (found["properties"]?["parameters"]?["associations"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(association => Names(association["domains"]))]
            : [];

    public bool HasRoute(string route) => _routes.ContainsKey(route);

    public bool HasPolicy(string policy) => _policies.ContainsKey(policy);

    public JsonObject? Route(string route) => _routes.TryGetValue(route, out var found) ? found : null;

    public void ThrottleNext(int seconds)
    {
        lock (_gate)
        {
            _throttle = seconds;
        }
    }

    public void HangNext()
    {
        lock (_gate)
        {
            _hang = true;
        }
    }

    // The next request is refused the app's permission, as for a role granted moments ago that has not reached Resource
    // Manager yet.
    public void ForbidNext()
    {
        lock (_gate)
        {
            _forbid = true;
        }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization.ToString() != $"Bearer {Token}")
            {
                await Problem(HttpStatusCode.Unauthorized, "AuthenticationFailed", "Authentication failed.").ExecuteAsync(context);

                return;
            }

            if (context.Request.Query["api-version"] != ApiVersion)
            {
                await Problem(HttpStatusCode.BadRequest, "InvalidApiVersionParameter", "The api-version is not supported.").ExecuteAsync(context);

                return;
            }

            int? throttle;
            bool hang;
            bool forbid;

            lock (_gate)
            {
                (throttle, _throttle) = (_throttle, null);
                (hang, _hang) = (_hang, false);
                (forbid, _forbid) = (_forbid, false);
            }

            if (forbid)
            {
                await Problem(HttpStatusCode.Forbidden, "AuthorizationFailed", "The client does not have authorization to perform this action.").ExecuteAsync(context);

                return;
            }

            if (hang)
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }

            if (throttle is { } seconds)
            {
                context.Response.Headers.RetryAfter = $"{seconds}";
                await Problem(HttpStatusCode.TooManyRequests, "TooManyRequests", "The request is being throttled.").ExecuteAsync(context);

                return;
            }

            await next(context);
        });

        var profile = app.MapGroup($"/subscriptions/{Subscription}/resourceGroups/{Group}/providers/Microsoft.Cdn/profiles/{Profile}");

        profile.MapGet("/customDomains", () =>
            Results.Json(new JsonObject { ["value"] = new JsonArray([.. _domains.Values.Select(domain => (JsonNode)Describe(Advance(domain)))]) }));

        profile.MapGet("/customDomains/{name}", (string name) =>
            _domains.TryGetValue(name, out var domain) ? Results.Json(Describe(Advance(domain))) : NotFound(name));

        profile.MapPut("/customDomains/{name}", async (string name, HttpContext context) =>
        {
            var properties = (await Body(context))["properties"] as JsonObject ?? [];
            var hostName = properties["hostName"]?.GetValue<string>() ?? string.Empty;

            if (properties["tlsSettings"]?["certificateType"]?.GetValue<string>() != "ManagedCertificate"
                || properties["tlsSettings"]?["minimumTlsVersion"]?.GetValue<string>() != "TLS12")
            {
                return Problem(HttpStatusCode.BadRequest, "BadRequest", "A domain here has a managed certificate and TLS 1.2 at least.");
            }

            if (_elsewhere.ContainsKey(hostName))
            {
                return Problem(HttpStatusCode.BadRequest, "BadRequest", $"The domain {hostName} is already in use by another Front Door profile.");
            }

            var made = !_domains.ContainsKey(name);
            var domain = _domains.GetOrAdd(name, _ => new FakeEdgeDomain(name, hostName)
            {
                Token = $"token-{Guid.NewGuid():N}",
                TokenExpires = _clock.GetUtcNow() + TokenLasts,
                Provisioning = "Creating",
            });

            return Results.Json(Describe(domain), statusCode: made ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });

        profile.MapPost("/customDomains/{name}/refreshValidationToken", (string name) =>
        {
            if (!_domains.TryGetValue(name, out var domain))
            {
                return NotFound(name);
            }

            domain.Validation = "RefreshingValidationToken";
            domain.Token = $"token-{Guid.NewGuid():N}";
            domain.TokenExpires = _clock.GetUtcNow() + TokenLasts;

            return Results.Accepted();
        });

        profile.MapDelete("/customDomains/{name}", (string name) =>
        {
            if (!_domains.ContainsKey(name))
            {
                return Results.NoContent();
            }

            if (_routes.Values.Any(route => Names(route["properties"]?["customDomains"]).Contains(name, StringComparer.OrdinalIgnoreCase))
                || _policies.Keys.Any(policy => PolicyDomains(policy).Contains(name, StringComparer.OrdinalIgnoreCase)))
            {
                return Problem(HttpStatusCode.Conflict, "Conflict", "The custom domain is still associated with a route or a security policy.");
            }

            _domains.TryRemove(name, out _);

            return Results.Ok();
        });

        profile.MapGet("/afdEndpoints/{endpoint}/routes/{route}", async (string endpoint, string route, HttpContext context) =>
        {
            var found = endpoint == Endpoint && _routes.TryGetValue(route, out var existing) ? existing.DeepClone() : null;
            await Task.Delay(RouteReadTakes, context.RequestAborted);

            return found is null ? NotFound(route) : Results.Json(found);
        });

        profile.MapPut("/afdEndpoints/{endpoint}/routes/{route}", async (string endpoint, string route, HttpContext context) =>
        {
            var properties = Properties(await Body(context));
            var domains = Names(properties["customDomains"]);

            if (endpoint != Endpoint)
            {
                return NotFound(endpoint);
            }

            if (properties["linkToDefaultDomain"]?.GetValue<string>() != "Disabled")
            {
                return Problem(HttpStatusCode.BadRequest, "BadRequest", "The endpoint's own host name is already served by route 'platform' for this protocol and path.");
            }

            if (!Same(properties["originGroup"]?["id"], $"{ProfileId}/originGroups/app")
                || !Same((properties["ruleSets"] as JsonArray)?.FirstOrDefault()?["id"], $"{ProfileId}/ruleSets/caching"))
            {
                return Problem(HttpStatusCode.BadRequest, "BadRequest", "The route names an origin group or a rule set this profile does not have.");
            }

            if (Unusable(domains) is { } refusal)
            {
                return refusal;
            }

            var made = !_routes.ContainsKey(route);
            properties["provisioningState"] = "Succeeded";
            properties["deploymentStatus"] = "NotStarted";
            properties["endpointName"] = Endpoint;
            properties["customDomains"] = new JsonArray([.. domains.Select(name => (JsonNode)new JsonObject { ["id"] = DomainId(name), ["isActive"] = true })]);
            _routes[route] = new JsonObject
            {
                ["id"] = $"{ProfileId}/afdendpoints/{Endpoint}/routes/{route}".ToLowerInvariant(),
                ["name"] = route,
                ["type"] = "Microsoft.Cdn/profiles/afdendpoints/routes",
                ["properties"] = properties,
            };
            Writes.Enqueue($"PUT route {route}");

            foreach (var domain in _domains.Values)
            {
                domain.OnRouteSince = domains.Contains(domain.Name, StringComparer.OrdinalIgnoreCase) ? domain.OnRouteSince ?? _clock.GetUtcNow() : null;
            }

            return Results.Json(_routes[route], statusCode: made ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });

        profile.MapDelete("/afdEndpoints/{endpoint}/routes/{route}", (string endpoint, string route) =>
        {
            if (_routes.TryRemove(route, out _))
            {
                Writes.Enqueue($"DELETE route {route}");
            }

            foreach (var domain in _domains.Values)
            {
                domain.OnRouteSince = null;
            }

            return Results.Ok();
        });

        profile.MapGet("/securityPolicies/{policy}", (string policy) =>
            _policies.TryGetValue(policy, out var found) ? Results.Json(found) : NotFound(policy));

        profile.MapPut("/securityPolicies/{policy}", async (string policy, HttpContext context) =>
        {
            var properties = Properties(await Body(context));
            var parameters = properties["parameters"] as JsonObject ?? [];
            var associations = parameters["associations"] as JsonArray ?? [];
            var domains = associations.OfType<JsonObject>().SelectMany(association => Names(association["domains"])).ToList();

            if (parameters["type"]?.GetValue<string>() != "WebApplicationFirewall" || !Same(parameters["wafPolicy"]?["id"], WafPolicyId))
            {
                return Problem(HttpStatusCode.BadRequest, "BadRequest", "The security policy names a firewall policy this deployment does not have.");
            }

            if (Unusable(domains) is { } refusal)
            {
                return refusal;
            }

            var made = !_policies.ContainsKey(policy);
            properties["provisioningState"] = "Succeeded";
            properties["deploymentStatus"] = "NotStarted";
            properties["profileName"] = Profile;
            _policies[policy] = new JsonObject
            {
                ["id"] = $"{ProfileId}/securitypolicies/{policy}".ToLowerInvariant(),
                ["name"] = policy,
                ["type"] = "Microsoft.Cdn/profiles/securitypolicies",
                ["properties"] = properties,
            };
            Writes.Enqueue($"PUT policy {policy}");

            return Results.Json(_policies[policy], statusCode: made ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });

        profile.MapDelete("/securityPolicies/{policy}", (string policy) =>
        {
            if (_policies.TryRemove(policy, out _))
            {
                Writes.Enqueue($"DELETE policy {policy}");
            }

            return Results.Ok();
        });
    }

    // A route or a policy names at least one domain, each of this profile's.
    private IResult? Unusable(List<string> domains) =>
        domains.Count == 0 ? Problem(HttpStatusCode.BadRequest, "BadRequest", "At least one domain is required.")
        : domains.FirstOrDefault(name => !_domains.ContainsKey(name)) is { } missing ? Problem(HttpStatusCode.BadRequest, "BadRequest", $"The domain {missing} does not exist.")
        : null;

    // A read is when the fake catches up with the world: a token found in public DNS approves the domain, a token that
    // ran out times it out, and a certificate is issued a while after its domain is approved and routed.
    private FakeEdgeDomain Advance(FakeEdgeDomain domain)
    {
        var now = _clock.GetUtcNow();

        lock (domain)
        {
            domain.Provisioning = "Succeeded";

            if (domain.Validation is "Submitting" or "RefreshingValidationToken")
            {
                domain.Validation = "Pending";
            }

            if (domain.Validation is "Pending" or "PendingRevalidation")
            {
                if (domain.TokenExpires > now && domain.Token is { } token && _publicTxt(ValidationName(domain.HostName)).Contains(token))
                {
                    domain.Validation = "Approved";
                    domain.ApprovedAt = now;
                }
                else if (domain.Validation == "Pending" && domain.TokenExpires <= now)
                {
                    domain.Validation = "TimedOut";
                }
            }

            if (domain.Validation == "Approved" && !domain.CertificateIssued && domain.OnRouteSince is { } routed)
            {
                var since = routed > domain.ApprovedAt ? routed : domain.ApprovedAt!.Value;
                domain.CertificateIssued = now - since >= CertificateTakes;
                domain.Deployment = domain.CertificateIssued ? "Succeeded" : "InProgress";
                domain.IssuedAt = domain.CertificateIssued ? now : null;
            }
        }

        return domain;
    }

    private static string ValidationName(string hostName) =>
        $"_dnsauth.{(hostName.StartsWith("*.", StringComparison.Ordinal) ? hostName[2..] : hostName)}";

    private static JsonObject Describe(FakeEdgeDomain domain) => new()
    {
        ["id"] = DomainId(domain.Name),
        ["name"] = domain.Name,
        ["type"] = "Microsoft.Cdn/profiles/customdomains",
        ["properties"] = new JsonObject
        {
            ["profileName"] = Profile,
            ["hostName"] = domain.HostName,
            ["tlsSettings"] = new JsonObject
            {
                ["certificateType"] = "ManagedCertificate",
                ["minimumTlsVersion"] = "TLS12",
                ["secret"] = new JsonObject { ["id"] = domain.CertificateIssued ? $"{ProfileId}/secrets/{domain.Name}-managed".ToLowerInvariant() : string.Empty },
            },
            ["azureDnsZone"] = new JsonObject { ["id"] = string.Empty },
            ["preValidatedCustomDomainResourceId"] = new JsonObject { ["id"] = string.Empty },
            ["validationProperties"] = new JsonObject
            {
                ["validationToken"] = domain.Token ?? string.Empty,
                ["expirationDate"] = domain.TokenExpires?.ToString("O") ?? string.Empty,
            },
            ["domainValidationState"] = domain.Validation,
            ["provisioningState"] = domain.Provisioning,
            ["deploymentStatus"] = domain.Deployment,
        },
    };

    private static string DomainId(string name) => $"{ProfileId}/customdomains/{name}".ToLowerInvariant();

    private static List<string> Names(JsonNode? references) =>
        [.. (references as JsonArray ?? []).OfType<JsonObject>().Select(reference => reference["id"]?.GetValue<string>()?.Split('/')[^1]).OfType<string>()];

    private static bool Same(JsonNode? id, string expected) =>
        string.Equals(id?.GetValue<string>(), expected, StringComparison.OrdinalIgnoreCase);

    // The request's properties, let go of by the request so they can be kept.
    private static JsonObject Properties(JsonObject body) => body["properties"]?.DeepClone() as JsonObject ?? [];

    private static async Task<JsonObject> Body(HttpContext context) =>
        await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject ?? [];

    private static IResult NotFound(string name) =>
        Problem(HttpStatusCode.NotFound, "NotFound", $"The resource '{name}' was not found.");

    private static IResult Problem(HttpStatusCode status, string code, string message) =>
        Results.Json(new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }, statusCode: (int)status);
}

internal sealed class FakeEdgeDomain(string name, string hostName)
{
    public string Name { get; } = name;

    public string HostName { get; } = hostName;

    public string Validation { get; set; } = "Submitting";

    public string? Token { get; set; }

    public DateTimeOffset? TokenExpires { get; set; }

    public string Provisioning { get; set; } = "Succeeded";

    public string Deployment { get; set; } = "NotStarted";

    public bool CertificateIssued { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? OnRouteSince { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }
}
