using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShopForge.Infrastructure.Email;

// The parts of Mailgun's API a store's own sending domain needs: the domain, its DNS records and its verification,
// and a sending key that works for that domain alone (D-211). The shapes are Mailgun's own, as its API reference and
// official Go client describe them. It is called with the deployment's account key, which has to be one that may
// manage domains and keys.
internal sealed class MailgunApi(HttpClient httpClient)
{
    // A selector of the platform's own, so the store's DKIM key never shares a name with one somebody else published.
    public const string DkimSelector = "shopforge";

    public async Task<MailgunDomain?> FindDomainAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"v4/domains/{Uri.EscapeDataString(name)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadAsync(response, cancellationToken);

        return MailgunDomain.From(document);
    }

    public async Task<MailgunDomain> CreateDomainAsync(string name, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
            { new StringContent("2048"), "dkim_key_size" },
            { new StringContent(DkimSelector), "dkim_selector" },
        };
        using var response = await httpClient.PostAsync("v4/domains", content, cancellationToken);
        using var document = await ReadAsync(response, cancellationToken);

        return MailgunDomain.From(document);
    }

    // Asks Mailgun to look at the domain's DNS now, rather than whenever it next would.
    public async Task<MailgunDomain> VerifyDomainAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsync($"v4/domains/{Uri.EscapeDataString(name)}/verify", content: null, cancellationToken);
        using var document = await ReadAsync(response, cancellationToken);

        return MailgunDomain.From(document);
    }

    // Gone already is gone.
    public async Task DeleteDomainAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"v3/domains/{Uri.EscapeDataString(name)}", cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await ReadAsync(response, cancellationToken);
        }
    }

    // A listed key never carries its secret: Mailgun shows that once, when the key is made.
    public async Task<IReadOnlyList<MailgunKey>> SendingKeysAsync(string domain, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"v1/keys?domain_name={Uri.EscapeDataString(domain)}&kind=domain", cancellationToken);
        using var document = await ReadAsync(response, cancellationToken);

        return document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Select(item => new MailgunKey(Text(item, "id") ?? string.Empty, Text(item, "description") ?? string.Empty))]
            : [];
    }

    public async Task<MailgunKeySecret> CreateSendingKeyAsync(string domain, string description, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("sending"), "role" },
            { new StringContent("domain"), "kind" },
            { new StringContent(domain), "domain_name" },
            { new StringContent(description), "description" },
        };
        using var response = await httpClient.PostAsync("v1/keys", content, cancellationToken);
        using var document = await ReadAsync(response, cancellationToken);
        var key = document.RootElement.GetProperty("key");

        return new MailgunKeySecret(
            Text(key, "id") ?? throw new MailgunException(HttpStatusCode.BadGateway, "Mailgun made a key without saying which."),
            Text(key, "secret") ?? throw new MailgunException(HttpStatusCode.BadGateway, "Mailgun made a key without handing over its secret."));
    }

    public async Task DeleteKeyAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"v1/keys/{Uri.EscapeDataString(id)}", cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await ReadAsync(response, cancellationToken);
        }
    }

    public static void Configure(HttpClient httpClient, EmailOptions options)
    {
        httpClient.BaseAddress = new Uri(options.Mailgun.BaseUrl.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Authorization = Basic(options.Mailgun.ApiKey);
    }

    public static AuthenticationHeaderValue Basic(string key) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{key}")));

    // Which of Mailgun's regions a base address is, which is where a domain made through it has to send.
    public static string RegionOf(EmailOptions options) =>
        new Uri(options.Mailgun.BaseUrl).Host.Contains(".eu.", StringComparison.OrdinalIgnoreCase) ? "eu" : "us";

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MailgunException(response.StatusCode, MessageOf(body));
        }

        return JsonDocument.Parse(body.Length == 0 ? "{}" : body);
    }

    private static string MessageOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return Text(document.RootElement, "message") ?? Shorten(body);
        }
        catch (JsonException)
        {
            return Shorten(body);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Shorten(string text) => text.Length > 200 ? text[..200] : text;
}

// A domain as Mailgun describes it, its records kept exactly as they came.
internal sealed record MailgunDomain(string Name, string State, string Records)
{
    public bool IsActive => State == "active";

    public static MailgunDomain From(JsonDocument document)
    {
        var root = document.RootElement;
        var domain = root.GetProperty("domain");
        var sending = root.TryGetProperty("sending_dns_records", out var sent) ? sent.GetRawText() : "[]";
        var receiving = root.TryGetProperty("receiving_dns_records", out var received) ? received.GetRawText() : "[]";

        return new MailgunDomain(
            domain.GetProperty("name").GetString()!,
            domain.GetProperty("state").GetString()!,
            $$"""{"sending":{{sending}},"receiving":{{receiving}}}""");
    }
}

internal sealed record MailgunKey(string Id, string Description);

internal sealed record MailgunKeySecret(string Id, string Secret);

// A refusal from Mailgun. One it would give again, a 4xx other than a timeout or a rate limit, stops an operation
// for a person; anything else is tried again (D-210).
internal sealed class MailgunException(HttpStatusCode status, string message) : Exception($"Mailgun answered {(int)status}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public bool IsPermanent => (int)Status is >= 400 and < 500 && Status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
}
