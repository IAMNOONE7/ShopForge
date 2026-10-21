using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Provisioning;

namespace ShopForge.Infrastructure.Dns;

// Zones and records at Cloudflare, through its v4 API and an API token (D-212). The shapes are Cloudflare's, as its API
// reference gives them: the envelope every answer comes in, a zone's status and the nameservers it was given, records
// found by exact name and type, and TXT content as quoted character strings. Records the platform writes say so in
// their comment; tags would be tidier but need a paid plan.
internal sealed class CloudflareZones(HttpClient httpClient, CloudflareOptions options, TimeProvider clock) : IDnsZones
{
    private const string Provider = "Cloudflare";

    // What Cloudflare does to a client that spends its quota: blocks it for five minutes. Waited out when a 429 comes
    // without saying how long.
    private static readonly TimeSpan Blocked = TimeSpan.FromMinutes(5);

    public bool IsConfigured => true;

    public static void Configure(HttpClient httpClient, CloudflareOptions options)
    {
        httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiToken);
    }

    public async Task<DnsZone?> FindAsync(string name, CancellationToken cancellationToken)
    {
        var found = await SendAsync(HttpMethod.Get, $"zones?name={Uri.EscapeDataString(name)}&account.id={Uri.EscapeDataString(options.AccountId)}", body: null, cancellationToken);

        return Results(found).Select(Zone).FirstOrDefault(zone => string.Equals(zone.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DnsZone> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["account"] = new JsonObject { ["id"] = options.AccountId },
            ["type"] = "full",
        };

        return Zone(Result(await SendAsync(HttpMethod.Post, "zones", body, cancellationToken)));
    }

    public async Task<DnsZone?> ZoneAsync(string zoneId, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Get, $"zones/{Uri.EscapeDataString(zoneId)}", body: null, cancellationToken, notFoundIsNull: true) is { } found
            ? Zone(Result(found))
            : null;

    public Task CheckDelegationAsync(string zoneId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"zones/{Uri.EscapeDataString(zoneId)}/activation_check", body: null, cancellationToken);

    public Task DeleteAsync(string zoneId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"zones/{Uri.EscapeDataString(zoneId)}", body: null, cancellationToken, notFoundIsNull: true);

    // Asked from the longest name down, so a zone the platform runs for `shops.example.cz` wins over one for
    // `example.cz`. A single label is never a zone the platform could run.
    public async Task<DnsZone?> HoldingAsync(string hostName, CancellationToken cancellationToken)
    {
        var labels = hostName.TrimEnd('.').Split('.');

        for (var start = 0; start <= labels.Length - 2; start++)
        {
            if (await FindAsync(string.Join('.', labels[start..]), cancellationToken) is { Status: DnsZoneStatus.Active } zone)
            {
                return zone;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<DnsRecord>> RecordsAsync(string zoneId, string name, string type, CancellationToken cancellationToken)
    {
        var listed = await SendAsync(
            HttpMethod.Get,
            $"zones/{Uri.EscapeDataString(zoneId)}/dns_records?name.exact={Uri.EscapeDataString(name)}&type={Uri.EscapeDataString(type)}&per_page=100",
            body: null,
            cancellationToken);

        return [.. Results(listed).Select(Record)];
    }

    public async Task<DnsRecordChange> EnsureAsync(string zoneId, DnsRecordSpec record, CancellationToken cancellationToken)
    {
        var existing = await RecordsAsync(zoneId, record.Name, record.Type, cancellationToken);
        var kept = existing.FirstOrDefault(candidate => Same(candidate.Value, record.Value, record.Type));
        var written = false;

        if (kept is null)
        {
            kept = existing.Count > 0
                ? Record(Result(await SendAsync(HttpMethod.Put, RecordPath(zoneId, existing[0].Id), Body(record), cancellationToken)))
                : Record(Result(await SendAsync(HttpMethod.Post, $"zones/{Uri.EscapeDataString(zoneId)}/dns_records", Body(record), cancellationToken)));
            written = true;
        }

        foreach (var extra in existing.Where(candidate => candidate.Id != kept.Id))
        {
            await SendAsync(HttpMethod.Delete, RecordPath(zoneId, extra.Id), body: null, cancellationToken, notFoundIsNull: true);
            written = true;
        }

        return new DnsRecordChange(kept, written);
    }

    public async Task<int> RemoveAsync(string zoneId, string name, string type, string? value, CancellationToken cancellationToken)
    {
        var removed = 0;

        foreach (var record in await RecordsAsync(zoneId, name, type, cancellationToken))
        {
            if (value is null || Same(record.Value, value, type))
            {
                await SendAsync(HttpMethod.Delete, RecordPath(zoneId, record.Id), body: null, cancellationToken, notFoundIsNull: true);
                removed++;
            }
        }

        return removed;
    }

    // A TXT value is said as one or more quoted strings; anything else as it is.
    public static string Quote(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    // The text a TXT record carries: its quoted strings joined, as a resolver joins them. Content that was never quoted
    // is taken as it is.
    public static string Unquote(string content)
    {
        var trimmed = content.Trim();

        if (!trimmed.StartsWith('"'))
        {
            return trimmed;
        }

        var text = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];

            if (character == '\\' && quoted && index + 1 < trimmed.Length)
            {
                text.Append(trimmed[++index]);
            }
            else if (character == '"')
            {
                quoted = !quoted;
            }
            else if (quoted)
            {
                text.Append(character);
            }
        }

        return text.ToString();
    }

    // A token is compared exactly; a host name the way DNS compares one.
    private static bool Same(string left, string right, string type) =>
        type == "TXT"
            ? string.Equals(left, right, StringComparison.Ordinal)
            : string.Equals(left.TrimEnd('.'), right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static string RecordPath(string zoneId, string recordId) =>
        $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(recordId)}";

    // Never proxied: Cloudflare is the platform's DNS only, and Front Door is the edge (§7.1).
    private static JsonObject Body(DnsRecordSpec record)
    {
        var body = new JsonObject
        {
            ["type"] = record.Type,
            ["name"] = record.Name,
            ["content"] = record.Type == "TXT" ? Quote(record.Value) : record.Value,
            ["ttl"] = record.Ttl,
            ["comment"] = record.Comment,
        };

        if (record.Type is "CNAME" or "A" or "AAAA")
        {
            body["proxied"] = false;
        }

        return body;
    }

    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken, bool notFoundIsNull = false)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull)
        {
            return null;
        }

        var envelope = Parse(text);

        if (!response.IsSuccessStatusCode || envelope?["success"] is JsonValue success && success.TryGetValue<bool>(out var succeeded) && !succeeded)
        {
            var status = response.IsSuccessStatusCode ? (int)HttpStatusCode.BadRequest : (int)response.StatusCode;
            var wait = status == (int)HttpStatusCode.TooManyRequests ? ProviderHeaders.RetryAfter(response, clock.GetUtcNow()) ?? Blocked : (TimeSpan?)null;

            throw new ProviderException(Provider, status, MessageOf(envelope, text), wait);
        }

        return envelope;
    }

    private static JsonObject? Parse(string text)
    {
        try
        {
            return text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MessageOf(JsonObject? envelope, string text) =>
        (envelope?["errors"] as JsonArray)?.OfType<JsonObject>().Select(error => $"{Text(error, "message")} ({error["code"]})").FirstOrDefault()
        ?? (text.Length > 200 ? text[..200] : text);

    private static JsonObject Result(JsonObject? envelope) =>
        envelope?["result"] as JsonObject ?? throw new ProviderException(Provider, (int)HttpStatusCode.BadGateway, "Cloudflare answered without a result.");

    private static IEnumerable<JsonObject> Results(JsonObject? envelope) => (envelope?["result"] as JsonArray ?? []).OfType<JsonObject>();

    private static DnsZone Zone(JsonObject zone) => new(
        Text(zone, "id") ?? string.Empty,
        Text(zone, "name") ?? string.Empty,
        Text(zone, "status") switch
        {
            "active" => DnsZoneStatus.Active,
            "moved" => DnsZoneStatus.Moved,
            _ => DnsZoneStatus.Pending,
        },
        [.. (zone["name_servers"] as JsonArray ?? []).Select(server => server?.GetValue<string>()).OfType<string>()]);

    private static DnsRecord Record(JsonObject record)
    {
        var type = Text(record, "type") ?? string.Empty;
        var content = Text(record, "content") ?? string.Empty;

        return new DnsRecord(
            Text(record, "id") ?? string.Empty,
            Text(record, "name") ?? string.Empty,
            type,
            type == "TXT" ? Unquote(content) : content,
            Text(record, "comment"));
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
