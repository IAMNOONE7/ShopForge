using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShopForge.IntegrationTests.Email;

// Mailgun's domain, key and sending calls, over HTTP, answered in the shapes Mailgun's API reference and its official
// client describe (D-211). The account key may do anything; a domain's sending key may only send for that domain.
// A test says when a domain's records are published and can make one answer go missing on the way back.
internal sealed class RecordedMailgun : IAsyncDisposable
{
    public const string AccountKey = "account-key-made-up";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, FakeDomain> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FakeKey> _keys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _elsewhere = new(StringComparer.OrdinalIgnoreCase);
    private int _loseNextKeyAnswer;
    private int _failNextKeyDelete;

    private RecordedMailgun(WebApplication app) => _app = app;

    public string BaseUrl { get; private set; } = string.Empty;

    public ConcurrentQueue<SentMessage> Sent { get; } = new();

    public IReadOnlyCollection<FakeDomain> Domains => [.. _domains.Values];

    public IReadOnlyCollection<FakeKey> Keys => [.. _keys.Values];

    public static async Task<RecordedMailgun> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var mailgun = new RecordedMailgun(app);
        mailgun.Map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        mailgun.BaseUrl = app.Urls.Single();

        return mailgun;
    }

    // What a merchant does at their DNS host: Mailgun finds every record at the next look.
    public void Publish(string domain) => _domains[domain].Published = true;

    // Mailgun makes the key and the answer never arrives, as when the connection drops on the way back.
    public void LoseNextKeyAnswer() => Interlocked.Exchange(ref _loseNextKeyAnswer, 1);

    // Mailgun is down for the next key it is asked to delete.
    public void FailNextKeyDelete() => Interlocked.Exchange(ref _failNextKeyDelete, 1);

    // A domain in somebody else's Mailgun account: not ours to see, and not ours to add.
    public void TakenElsewhere(string domain) => _elsewhere[domain] = true;

    // A domain somebody added to the account before, with no operation of ours behind it.
    public FakeDomain AddExisting(string domain) => _domains[domain] = FakeDomain.Made(domain, "shopforge");

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private void Map(WebApplication app)
    {
        app.MapGet("/v4/domains/{name}", (string name, HttpRequest request) =>
            !IsAccount(request) ? Unauthorized()
            : _domains.TryGetValue(name, out var domain) ? Results.Json(domain.Describe("Domain DNS records have been found"))
            : Results.Json(new { message = "Domain not found" }, statusCode: 404));

        app.MapPost("/v4/domains", async (HttpRequest request) =>
        {
            if (!IsAccount(request))
            {
                return Unauthorized();
            }

            var form = await request.ReadFormAsync();
            var name = form["name"].ToString();

            if (_domains.ContainsKey(name) || _elsewhere.ContainsKey(name))
            {
                return Results.Json(new { message = "This domain name is already taken" }, statusCode: 400);
            }

            var domain = _domains[name] = FakeDomain.Made(name, form["dkim_selector"].ToString());

            return Results.Json(domain.Describe("Domain DNS records have been created"));
        });

        app.MapPut("/v4/domains/{name}/verify", (string name, HttpRequest request) =>
        {
            if (!IsAccount(request))
            {
                return Unauthorized();
            }

            if (!_domains.TryGetValue(name, out var domain))
            {
                return Results.Json(new { message = "Domain not found" }, statusCode: 404);
            }

            domain.Verifications++;

            return Results.Json(domain.Describe("Domain DNS records have been updated"));
        });

        app.MapDelete("/v3/domains/{name}", (string name, HttpRequest request) =>
            !IsAccount(request) ? Unauthorized()
            : _domains.TryRemove(name, out _) ? Results.Json(new { message = "Domain has been deleted" })
            : Results.Json(new { message = "Domain not found" }, statusCode: 404));

        app.MapGet("/v1/keys", (string? domain_name, string? kind, HttpRequest request) =>
        {
            if (!IsAccount(request))
            {
                return Unauthorized();
            }

            var items = _keys.Values
                .Where(key => (domain_name is null || key.Domain == domain_name) && (kind is null || kind == "domain"))
                .Select(key => key.Listed())
                .ToList();

            return Results.Json(new { total_count = items.Count, items });
        });

        app.MapPost("/v1/keys", async (HttpRequest request) =>
        {
            if (!IsAccount(request))
            {
                return Unauthorized();
            }

            var form = await request.ReadFormAsync();

            if (form["role"] != "sending" || form["kind"] != "domain" || !_domains.ContainsKey(form["domain_name"].ToString()))
            {
                return Results.Json(new { message = "A sending key is made for a domain of the account." }, statusCode: 400);
            }

            var key = new FakeKey($"key-{Guid.NewGuid():N}"[..20], form["domain_name"].ToString(), form["description"].ToString(), $"secret-{Guid.NewGuid():N}");
            _keys[key.Id] = key;

            return Interlocked.Exchange(ref _loseNextKeyAnswer, 0) == 1
                ? Results.Json(new { message = "upstream connect error" }, statusCode: 503)
                : Results.Json(new { message = "great success", key = key.Created() });
        });

        app.MapDelete("/v1/keys/{id}", (string id, HttpRequest request) =>
            !IsAccount(request) ? Unauthorized()
            : Interlocked.Exchange(ref _failNextKeyDelete, 0) == 1 ? Results.Json(new { message = "upstream connect error" }, statusCode: 503)
            : _keys.TryRemove(id, out _) ? Results.Json(new { message = "key deleted" })
            : Results.Json(new { message = "key not found" }, statusCode: 404));

        app.MapPost("/v3/{domain}/messages", async (string domain, HttpRequest request) =>
        {
            var key = KeyOf(request);

            if (key != AccountKey && !_keys.Values.Any(candidate => candidate.Secret == key && candidate.Domain == domain))
            {
                return Results.Json(new { message = "Invalid private key" }, statusCode: 401);
            }

            var form = await request.ReadFormAsync();
            Sent.Enqueue(new SentMessage(
                domain,
                key == AccountKey ? "account" : _keys.Values.Single(candidate => candidate.Secret == key).Id,
                form["from"].ToString(),
                form["to"].ToString(),
                form["h:Reply-To"].ToString() is { Length: > 0 } replyTo ? replyTo : null,
                form["v:shopforge-store"].ToString() is { Length: > 0 } store ? store : null));

            return Results.Json(new { id = $"<{Guid.NewGuid():N}@{domain}>", message = "Queued. Thank you." });
        });
    }

    private static IResult Unauthorized() => Results.Json(new { message = "Invalid private key" }, statusCode: 401);

    private static bool IsAccount(HttpRequest request) => KeyOf(request) == AccountKey;

    private static string? KeyOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();

        if (!header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            return null;
        }

        var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));

        return credentials.StartsWith("api:", StringComparison.Ordinal) ? credentials["api:".Length..] : null;
    }
}

internal sealed class FakeDomain
{
    private FakeDomain(string name, string selector)
    {
        Name = name;
        Selector = selector;
    }

    public string Name { get; }

    public string Selector { get; }

    public bool Published { get; set; }

    public int Verifications { get; set; }

    public static FakeDomain Made(string name, string selector) => new(name, selector);

    // The records a sending domain needs, with Mailgun's own fields: a TXT for SPF on the domain, the DKIM key under
    // the selector, the tracking CNAME, and the two MX records for what comes back.
    public JsonArray Sending() =>
    [
        Record("TXT", Name, "v=spf1 include:mailgun.org ~all"),
        Record("TXT", $"{Selector}._domainkey.{Name}", "k=rsa; p=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0made0up"),
        Record("CNAME", $"email.{Name}", "eu.mailgun.org"),
    ];

    public JsonArray Receiving() =>
    [
        Record("MX", null, "mxa.eu.mailgun.org", "10"),
        Record("MX", null, "mxb.eu.mailgun.org", "10"),
    ];

    public JsonObject Describe(string message) => new()
    {
        ["message"] = message,
        ["domain"] = new JsonObject
        {
            ["created_at"] = "Tue, 20 Oct 2026 09:00:00 GMT",
            ["id"] = "5f7d1b6a0c2e4f3a9b8c7d6e",
            ["name"] = Name,
            ["require_tls"] = false,
            ["smtp_login"] = $"postmaster@{Name}",
            ["spam_action"] = "disabled",
            ["state"] = Published ? "active" : "unverified",
            ["type"] = "custom",
            ["use_automatic_sender_security"] = false,
            ["web_prefix"] = "email",
            ["web_scheme"] = "http",
        },
        ["receiving_dns_records"] = Receiving(),
        ["sending_dns_records"] = Sending(),
    };

    private JsonObject Record(string type, string? name, string value, string? priority = null)
    {
        var record = new JsonObject
        {
            ["is_active"] = true,
            ["cached"] = new JsonArray(),
            ["record_type"] = type,
            ["valid"] = Published ? "valid" : "unknown",
            ["value"] = value,
        };

        if (name is not null)
        {
            record["name"] = name;
        }

        if (priority is not null)
        {
            record["priority"] = priority;
        }

        return record;
    }
}

internal sealed record FakeKey(string Id, string Domain, string Description, string Secret)
{
    // Listed, a key never shows its secret again.
    public object Listed() => new
    {
        id = Id,
        description = Description,
        kind = "domain",
        role = "sending",
        created_at = "2026-10-20T09:00:00",
        updated_at = "2026-10-20T09:00:00",
        requestor = (string?)null,
        user_name = (string?)null,
        domain_name = Domain,
        is_disabled = false,
    };

    public object Created() => new
    {
        id = Id,
        description = Description,
        kind = "domain",
        role = "sending",
        created_at = "2026-10-20T09:00:00",
        updated_at = "2026-10-20T09:00:00",
        requestor = (string?)null,
        user_name = (string?)null,
        domain_name = Domain,
        is_disabled = false,
        secret = Secret,
    };
}

// What reached Mailgun to be sent: through which domain, with which key, from and to whom, and for which store.
internal sealed record SentMessage(string Domain, string Key, string From, string To, string? ReplyTo, string? Store);
