namespace ShopForge.Infrastructure.Edge;

// Where the edge is and what the runtime may add to it (D-207, D-212): the deployment's Front Door profile and endpoint,
// the template's origin group, rule set and firewall policy that the runtime's route and security policy reuse, and the
// names of those two. Without all of them, no merchant's domain is bound and nothing is written to Azure.
internal sealed class EdgeDomainOptions
{
    public const string Section = "Edge:Domains";

    public string SubscriptionId { get; init; } = string.Empty;

    public string ResourceGroup { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    // The endpoint's own host name, e.g. shopforge-prod-endpoint-abc123.z01.azurefd.net, which a domain's CNAME names.
    public string EndpointHost { get; init; } = string.Empty;

    public string OriginGroup { get; init; } = "app";

    public string RuleSet { get; init; } = "caching";

    // The firewall policy's resource id; the runtime's security policy applies the template's one.
    public string WafPolicyId { get; init; } = string.Empty;

    public string Route { get; init; } = "merchants";

    public string SecurityPolicy { get; init; } = "merchants";

    public string ManagementUrl { get; init; } = "https://management.azure.com/";

    public bool IsConfigured =>
        new[] { SubscriptionId, ResourceGroup, Profile, Endpoint, EndpointHost, OriginGroup, RuleSet, WafPolicyId, Route, SecurityPolicy }
            .All(value => !string.IsNullOrWhiteSpace(value));

    // Some of the deployment's own names given and not all: a template that lost one, which would leave every merchant's
    // domain unbound without a word.
    public bool IsPartlyConfigured =>
        !IsConfigured && new[] { SubscriptionId, ResourceGroup, Profile, Endpoint, EndpointHost, WafPolicyId }.Any(value => !string.IsNullOrWhiteSpace(value));

    public string ProfileId =>
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.Cdn/profiles/{Profile}";

    // The token's audience is the management endpoint itself: a stand-in on this machine asks for its own.
    public string TokenScope => $"{new Uri(ManagementUrl).GetLeftPart(UriPartial.Authority)}/.default";
}
