namespace ShopForge.Infrastructure.Dns;

// The platform's Cloudflare account (D-212): a token that may edit zones and their DNS in that account alone, and the
// account the zones are made in. The token comes from the vault, never from a file.
internal sealed class CloudflareOptions
{
    public const string Section = "Dns:Cloudflare";

    public string ApiToken { get; init; } = string.Empty;

    public string AccountId { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.cloudflare.com/client/v4/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(AccountId);

    public bool IsPartlyConfigured => !IsConfigured && (!string.IsNullOrWhiteSpace(ApiToken) || !string.IsNullOrWhiteSpace(AccountId));
}
