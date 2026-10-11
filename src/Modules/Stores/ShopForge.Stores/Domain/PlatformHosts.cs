using ShopForge.Shared.Stores;

namespace ShopForge.Stores.Domain;

// The zone the platform owns, and the only place a store's own address is composed. A store is given a name
// under this suffix when it is created, and that name needs no proof because the platform controls the DNS for
// it. Every other name a merchant types is a claim and goes through the challenge (D-189).
//
// Before this existed, store creation took whatever host name it was handed and marked it proved. Host names are
// unique across the platform, so one company could take `www.somebody-elses-shop.cz` and the shop that owned it
// could never add it — no DNS control required, and no way for the victim to find out why.
internal sealed class PlatformHosts
{
    public const string Section = "StoreHosts";

    // A DNS label is 63 characters at most, whatever a slug allows.
    public const int MaxLabelLength = 63;

    // The suffix store subdomains are composed under: `shops.example.cz` in a deployment, `localhost` on this
    // machine. Empty means the platform has not been told what it owns, and then it hands out no names at all —
    // refusing is the only safe answer, because the alternative is trusting whatever arrives.
    public string Suffix { get; init; } = string.Empty;

    public bool IsConfigured => Normalized is not null;

    private string? Normalized => HostNames.Normalize(Suffix);

    // Whether a host name is the platform's to give away. Only a name under the suffix is, and the suffix itself
    // is not: that is the platform's own site, not a shop's.
    public bool Owns(string? hostName)
    {
        var normalized = HostNames.Normalize(hostName);

        // Ending in `.suffix` is the whole test: a normalized host name never starts with a dot, so a name that
        // ends in `.suffix` has at least one label of its own in front of it.
        return normalized is not null
            && Normalized is { } suffix
            && normalized.EndsWith($".{suffix}", StringComparison.Ordinal);
    }

    // The label out of whatever a caller gave: a bare `wooden-home`, or `wooden-home.shops.example.cz` with the
    // suffix the platform already knows. Anything else is null, which the caller turns into a refusal naming the
    // challenge route.
    public string? LabelOf(string? given)
    {
        if (string.IsNullOrWhiteSpace(given) || Normalized is not { } suffix)
        {
            return null;
        }

        var trimmed = given.Trim().TrimEnd('.').ToLowerInvariant();
        var label = Owns(trimmed) ? trimmed[..^(suffix.Length + 1)] : trimmed;

        return IsLabel(label) ? label : null;
    }

    public string Compose(string label) =>
        IsLabel(label) && Normalized is { } suffix
            ? $"{label}.{suffix}"
            : throw new ArgumentException($"'{label}' is not a label this platform can give out.", nameof(label));

    // The shapes DNS and a slug agree on: lower-case letters, digits and single hyphens, inside one label.
    private static bool IsLabel(string label) =>
        label.Length is > 0 and <= MaxLabelLength && !label.Contains('.', StringComparison.Ordinal) && Slugs.IsValid(label);
}
