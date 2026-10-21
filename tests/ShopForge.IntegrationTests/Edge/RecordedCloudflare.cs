using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShopForge.IntegrationTests.Edge;

// Cloudflare's zone and DNS record calls, over HTTP, answered in the shapes its v4 API reference gives (D-212): every
// answer in the envelope, a zone pending until the test delegates it, records found by exact name and type, and TXT
// content that has to arrive as quoted strings. A test can make the next answer a 429 with either of the headers
// Cloudflare sends, or make the next request hang until its caller gives up.
internal sealed class RecordedCloudflare : IAsyncDisposable
{
    public const string Token = "cloudflare-token-made-up";
    public const string Account = "0123456789abcdef0123456789abcdef";

    public static readonly string[] NameServers = ["ada.ns.cloudflare.com", "bob.ns.cloudflare.com"];

    private readonly WebApplication _app;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, FakeZone> _zones = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _hidden = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private (int Seconds, string Header)? _throttle;
    private bool _hang;

    private RecordedCloudflare(WebApplication app, TimeProvider clock)
    {
        _app = app;
        _clock = clock;
    }

    public string BaseUrl { get; private set; } = string.Empty;

    // Every record written, as "METHOD type name": what a test reads to say nothing was written twice.
    public ConcurrentQueue<string> Writes { get; } = new();

    // The same, with the test's time each was written at.
    public ConcurrentQueue<(string Write, DateTimeOffset At)> Timeline { get; } = new();

    public ConcurrentQueue<string> ActivationChecks { get; } = new();

    public static async Task<RecordedCloudflare> StartAsync(TimeProvider clock)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var cloudflare = new RecordedCloudflare(app, clock);
        cloudflare.Map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        cloudflare.BaseUrl = $"{app.Urls.Single()}/client/v4/";

        return cloudflare;
    }

    // A zone somebody already made in the platform's account.
    public FakeZone AddZone(string name, bool active)
    {
        var zone = new FakeZone(Guid.NewGuid().ToString("N"), name) { Active = active };
        _zones[zone.Id] = zone;

        return zone;
    }

    // What the merchant does at their registrar: the domain's nameservers now point at the zone.
    public void Delegate(string name) => ZoneNamed(name).Active = true;

    public FakeZone ZoneNamed(string name) => _zones.Values.Single(zone => zone.Name == name);

    public bool HasZone(string name) => _zones.Values.Any(zone => zone.Name == name);

    // A zone the world's resolvers do not answer for yet, however active: its nameservers have changed and the change
    // has not reached them.
    public void Hide(string name) => _hidden[name] = true;

    public void Show(string name) => _hidden.TryRemove(name, out _);

    // The next answer is a 429, saying how long to wait in `retry-after`, in the RateLimit header alone, or not at all.
    public void ThrottleNext(int seconds, string header = "retry-after")
    {
        lock (_gate)
        {
            _throttle = (seconds, header);
        }
    }

    public void HangNext()
    {
        lock (_gate)
        {
            _hang = true;
        }
    }

    // What the world's resolvers answer for a name: the TXT values of the active zone that holds it, longest first.
    public IReadOnlyList<string> PublicTxt(string name)
    {
        var zone = _zones.Values
            .Where(candidate => candidate.Active && !_hidden.ContainsKey(candidate.Name)
                && (name == candidate.Name || name.EndsWith($".{candidate.Name}", StringComparison.Ordinal)))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault();

        return zone is null
            ? []
            : [.. zone.Records.Values.Where(record => record.Type == "TXT" && record.Name == name).Select(record => record.Content.Trim('"'))];
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization.ToString() != $"Bearer {Token}")
            {
                await Envelope(context, HttpStatusCode.Forbidden, error: (9109, "Invalid access token"));

                return;
            }

            (int Seconds, string Header)? throttle;
            bool hang;

            lock (_gate)
            {
                (throttle, _throttle) = (_throttle, null);
                (hang, _hang) = (_hang, false);
            }

            if (hang)
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }

            if (throttle is { } limit)
            {
                if (limit.Header == "retry-after")
                {
                    context.Response.Headers["retry-after"] = $"{limit.Seconds}";
                }
                else if (limit.Header == "ratelimit")
                {
                    context.Response.Headers["Ratelimit"] = $"\"default\";r=0;t={limit.Seconds}";
                    context.Response.Headers["Ratelimit-Policy"] = "\"default\";q=1200;w=300";
                }

                await Envelope(context, HttpStatusCode.TooManyRequests, error: (971, "Please wait and consider throttling your request speed"));

                return;
            }

            await next(context);
        });

        var api = app.MapGroup("/client/v4");

        api.MapGet("/zones", (HttpContext context) =>
        {
            var name = context.Request.Query["name"].ToString();
            var account = context.Request.Query["account.id"].ToString();
            var zones = _zones.Values.Where(zone => (name.Length == 0 || zone.Name == name) && (account.Length == 0 || account == Account));

            return Answer(new JsonArray([.. zones.Select(zone => (JsonNode)zone.Describe())]));
        });

        api.MapPost("/zones", async (HttpContext context) =>
        {
            var body = await Body(context);
            var name = body["name"]?.GetValue<string>() ?? string.Empty;

            if (body["account"]?["id"]?.GetValue<string>() != Account || body["type"]?.GetValue<string>() != "full")
            {
                return Refuse(HttpStatusCode.BadRequest, 1001, "A full zone is made in the platform's account.");
            }

            if (_zones.Values.Any(zone => zone.Name == name))
            {
                return Refuse(HttpStatusCode.BadRequest, 1061, $"{name} already exists");
            }

            var zone = new FakeZone(Guid.NewGuid().ToString("N"), name);
            _zones[zone.Id] = zone;

            return Answer(zone.Describe());
        });

        api.MapGet("/zones/{id}", (string id) =>
            _zones.TryGetValue(id, out var zone) ? Answer(zone.Describe()) : Refuse(HttpStatusCode.NotFound, 7003, "Could not route to /zones"));

        api.MapPut("/zones/{id}/activation_check", (string id) =>
        {
            if (!_zones.TryGetValue(id, out var zone))
            {
                return Refuse(HttpStatusCode.NotFound, 7003, "Could not route to /zones");
            }

            ActivationChecks.Enqueue(zone.Name);

            return Answer(new JsonObject { ["id"] = id });
        });

        api.MapDelete("/zones/{id}", (string id) =>
            _zones.TryRemove(id, out _) ? Answer(new JsonObject { ["id"] = id }) : Refuse(HttpStatusCode.NotFound, 7003, "Could not route to /zones"));

        api.MapGet("/zones/{id}/dns_records", (string id, HttpContext context) =>
        {
            if (!_zones.TryGetValue(id, out var zone))
            {
                return Refuse(HttpStatusCode.NotFound, 7003, "Could not route to /zones");
            }

            var name = context.Request.Query["name.exact"].ToString();
            var type = context.Request.Query["type"].ToString();
            var records = zone.Records.Values.Where(record =>
                (name.Length == 0 || string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)) && (type.Length == 0 || record.Type == type));

            return Answer(new JsonArray([.. records.Select(record => (JsonNode)record.Describe())]));
        });

        api.MapPost("/zones/{id}/dns_records", async (string id, HttpContext context) =>
        {
            if (!_zones.TryGetValue(id, out var zone))
            {
                return Refuse(HttpStatusCode.NotFound, 7003, "Could not route to /zones");
            }

            var record = FakeRecord.From(Guid.NewGuid().ToString("N"), await Body(context));

            if (Invalid(zone, record) is { } refusal)
            {
                return refusal;
            }

            zone.Records[record.Id] = record;
            Written($"POST {record.Type} {record.Name}");

            return Answer(record.Describe());
        });

        api.MapPut("/zones/{id}/dns_records/{recordId}", async (string id, string recordId, HttpContext context) =>
        {
            if (!_zones.TryGetValue(id, out var zone) || !zone.Records.ContainsKey(recordId))
            {
                return Refuse(HttpStatusCode.NotFound, 81044, "Record does not exist.");
            }

            var record = FakeRecord.From(recordId, await Body(context));

            if (Invalid(zone, record) is { } refusal)
            {
                return refusal;
            }

            zone.Records[recordId] = record;
            Written($"PUT {record.Type} {record.Name}");

            return Answer(record.Describe());
        });

        api.MapDelete("/zones/{id}/dns_records/{recordId}", (string id, string recordId) =>
        {
            if (!_zones.TryGetValue(id, out var zone) || !zone.Records.TryRemove(recordId, out var record))
            {
                return Refuse(HttpStatusCode.NotFound, 81044, "Record does not exist.");
            }

            Written($"DELETE {record.Type} {record.Name}");

            return Answer(new JsonObject { ["id"] = recordId });
        });
    }

    private void Written(string write)
    {
        Writes.Enqueue(write);
        Timeline.Enqueue((write, _clock.GetUtcNow()));
    }

    // What Cloudflare refuses of a record: TXT content that is not quoted, and a CNAME beside another record of its name.
    private static IResult? Invalid(FakeZone zone, FakeRecord record)
    {
        if (record.Type == "TXT" && !(record.Content.StartsWith('"') && record.Content.EndsWith('"')))
        {
            return Refuse(HttpStatusCode.BadRequest, 9100, "The content must consist of quoted character strings.");
        }

        if (!(record.Name == zone.Name || record.Name.EndsWith($".{zone.Name}", StringComparison.Ordinal)))
        {
            return Refuse(HttpStatusCode.BadRequest, 9005, "The record's name is not in the zone.");
        }

        var others = zone.Records.Values.Where(other => other.Id != record.Id && other.Name == record.Name).ToList();

        return (record.Type == "CNAME" && others.Count > 0) || others.Any(other => other.Type == "CNAME")
            ? Refuse(HttpStatusCode.BadRequest, 81053, "An A, AAAA, or CNAME record with that host already exists.")
            : null;
    }

    private static async Task<JsonObject> Body(HttpContext context) =>
        await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject ?? [];

    private static IResult Answer(JsonNode result) => Results.Json(new JsonObject
    {
        ["success"] = true,
        ["errors"] = new JsonArray(),
        ["messages"] = new JsonArray(),
        ["result"] = result,
    });

    private static IResult Refuse(HttpStatusCode status, int code, string message) => Results.Json(
        new JsonObject
        {
            ["success"] = false,
            ["errors"] = new JsonArray(new JsonObject { ["code"] = code, ["message"] = message }),
            ["messages"] = new JsonArray(),
            ["result"] = null,
        },
        statusCode: (int)status);

    private static Task Envelope(HttpContext context, HttpStatusCode status, (int Code, string Message) error) =>
        Refuse(status, error.Code, error.Message).ExecuteAsync(context);
}

internal sealed class FakeZone(string id, string name)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool Active { get; set; }

    public ConcurrentDictionary<string, FakeRecord> Records { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<FakeRecord> RecordsNamed(string name, string type) =>
        [.. Records.Values.Where(record => record.Name == name && record.Type == type)];

    public JsonObject Describe() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["status"] = Active ? "active" : "pending",
        ["paused"] = false,
        ["type"] = "full",
        ["development_mode"] = 0,
        ["name_servers"] = new JsonArray([.. RecordedCloudflare.NameServers.Select(server => (JsonNode)server)]),
        ["original_name_servers"] = new JsonArray("ns1.registrar.test", "ns2.registrar.test"),
        ["original_registrar"] = null,
        ["original_dnshost"] = null,
        ["created_on"] = "2026-10-21T08:00:00.000000Z",
        ["modified_on"] = "2026-10-21T08:00:00.000000Z",
        ["activated_on"] = Active ? "2026-10-21T09:00:00.000000Z" : null,
        ["account"] = new JsonObject { ["id"] = RecordedCloudflare.Account, ["name"] = "ShopForge" },
        ["meta"] = new JsonObject { ["step"] = 2, ["phishing_detected"] = false },
    };
}

internal sealed record FakeRecord(string Id, string Type, string Name, string Content, int Ttl, bool Proxied, string? Comment)
{
    public static FakeRecord From(string id, JsonObject body) => new(
        id,
        body["type"]?.GetValue<string>() ?? string.Empty,
        body["name"]?.GetValue<string>() ?? string.Empty,
        body["content"]?.GetValue<string>() ?? string.Empty,
        body["ttl"]?.GetValue<int>() ?? 1,
        body["proxied"]?.GetValue<bool>() ?? false,
        body["comment"]?.GetValue<string>());

    public JsonObject Describe() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["type"] = Type,
        ["content"] = Content,
        ["proxiable"] = Type is "A" or "AAAA" or "CNAME",
        ["proxied"] = Proxied,
        ["ttl"] = Ttl,
        ["settings"] = new JsonObject(),
        ["meta"] = new JsonObject(),
        ["comment"] = Comment,
        ["tags"] = new JsonArray(),
        ["created_on"] = "2026-10-21T08:00:00.000000Z",
        ["modified_on"] = "2026-10-21T08:00:00.000000Z",
    };
}
