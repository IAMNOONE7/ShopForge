using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ShopForge.Shared.Edge;

namespace ShopForge.Infrastructure.Edge;

// Merchants' domains on Azure Front Door, through Azure Resource Manager (D-207, D-212). The shapes are Microsoft's, as
// its REST reference for 2025-04-15 gives them: a custom domain with a managed certificate, its validation token and
// the call that renews it, and the route and security policy whose domain lists only the runtime writes. The route and
// the policy are made with the first merchant's domain and deleted with the last, because the reference does not say
// either may stand empty. Every change to them is a read and a write under one lock, since neither has an ETag.
internal sealed class FrontDoorDomains(ArmApi arm, EdgeDomainOptions options, EdgeRouteLock routeLock) : IEdgeDomains
{
    private static readonly string[] ReadOnlyProperties = ["provisioningState", "deploymentStatus", "endpointName", "profileName"];

    public bool IsConfigured => true;

    public string TrafficTarget => options.EndpointHost;

    private string RoutePath => $"{options.ProfileId}/afdEndpoints/{options.Endpoint}/routes/{options.Route}";

    private string PolicyPath => $"{options.ProfileId}/securityPolicies/{options.SecurityPolicy}";

    // Named for its host and a hash of it: readable in the portal, the same on every replica and for every retry, and
    // never the name of a domain the template declares.
    public static string ResourceNameFor(string hostName)
    {
        var host = hostName.ToLowerInvariant();
        var readable = new string([.. host.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')]).Trim('-');
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(host)))[..10];

        return $"{EdgeNames.MerchantPrefix}{(readable.Length > 50 ? readable[..50].TrimEnd('-') : readable)}-{hash}";
    }

    public async Task<EdgeDomain?> FindAsync(string hostName, CancellationToken cancellationToken) =>
        await arm.GetAsync(DomainPath(hostName), cancellationToken) is { } found ? Read(found) : null;

    public async Task<EdgeDomain> CreateAsync(string hostName, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["hostName"] = hostName,
                ["tlsSettings"] = new JsonObject
                {
                    ["certificateType"] = "ManagedCertificate",
                    ["minimumTlsVersion"] = "TLS12",
                },
            },
        };

        return await arm.PutAsync(DomainPath(hostName), body, cancellationToken) is { } made
            ? Read(made)
            : await FindAsync(hostName, cancellationToken) ?? throw new InvalidOperationException($"Front Door made no domain for {hostName}.");
    }

    public Task RefreshValidationAsync(EdgeDomain domain, CancellationToken cancellationToken) =>
        arm.PostAsync($"{options.ProfileId}/customDomains/{domain.ResourceName}/refreshValidationToken", cancellationToken);

    public Task DeleteAsync(string hostName, CancellationToken cancellationToken) =>
        arm.DeleteAsync(DomainPath(hostName), cancellationToken);

    public async Task<bool> JoinAsync(string hostName, CancellationToken cancellationToken)
    {
        var domainId = DomainId(hostName);

        await using var held = await routeLock.HoldAsync(cancellationToken);
        var routeWritten = await JoinRouteAsync(domainId, cancellationToken);
        var policyWritten = await JoinPolicyAsync(domainId, cancellationToken);

        return routeWritten || policyWritten;
    }

    public async Task<bool> LeaveAsync(string hostName, CancellationToken cancellationToken)
    {
        var domainId = DomainId(hostName);

        await using var held = await routeLock.HoldAsync(cancellationToken);
        var policyWritten = await LeavePolicyAsync(domainId, cancellationToken);
        var routeWritten = await LeaveRouteAsync(domainId, cancellationToken);

        return routeWritten || policyWritten;
    }

    public async Task<EdgeMembership> MembershipAsync(string hostName, CancellationToken cancellationToken)
    {
        var domainId = DomainId(hostName);
        var route = await arm.GetAsync(RoutePath, cancellationToken);
        var policy = await arm.GetAsync(PolicyPath, cancellationToken);
        var onRoute = route is not null && RouteDomains(Properties(route)).Any(id => Same(id, domainId));
        var onPolicy = policy is not null && PolicyDomains(Properties(policy)).Any(id => Same(id, domainId));

        return new EdgeMembership(onRoute, onPolicy, onRoute && onPolicy && Settled(route!) && Settled(policy!));
    }

    public async Task<IReadOnlyList<EdgeDomain>> ListAsync(CancellationToken cancellationToken)
    {
        var domains = new List<EdgeDomain>();
        var next = $"{options.ProfileId}/customDomains";

        while (next is not null && await arm.GetAsync(next, cancellationToken) is { } page)
        {
            domains.AddRange((page["value"] as JsonArray ?? []).OfType<JsonObject>().Select(Read));
            next = Text(page, "nextLink");
        }

        return domains;
    }

    private async Task<bool> JoinRouteAsync(string domainId, CancellationToken cancellationToken)
    {
        if (await arm.GetAsync(RoutePath, cancellationToken) is not { } route)
        {
            await arm.PutAsync(RoutePath, NewRoute(domainId), cancellationToken);

            return true;
        }

        var properties = Writable(route);
        var domains = RouteDomains(properties);

        if (domains.Any(id => Same(id, domainId)))
        {
            return false;
        }

        properties["customDomains"] = References([.. domains, domainId]);
        await arm.PutAsync(RoutePath, new JsonObject { ["properties"] = properties }, cancellationToken);

        return true;
    }

    private async Task<bool> LeaveRouteAsync(string domainId, CancellationToken cancellationToken)
    {
        if (await arm.GetAsync(RoutePath, cancellationToken) is not { } route)
        {
            return false;
        }

        var properties = Writable(route);
        var domains = RouteDomains(properties);
        var rest = domains.Where(id => !Same(id, domainId)).ToList();

        if (rest.Count == domains.Count)
        {
            return false;
        }

        if (rest.Count == 0)
        {
            await arm.DeleteAsync(RoutePath, cancellationToken);

            return true;
        }

        properties["customDomains"] = References(rest);
        await arm.PutAsync(RoutePath, new JsonObject { ["properties"] = properties }, cancellationToken);

        return true;
    }

    private async Task<bool> JoinPolicyAsync(string domainId, CancellationToken cancellationToken)
    {
        if (await arm.GetAsync(PolicyPath, cancellationToken) is not { } policy)
        {
            await arm.PutAsync(PolicyPath, NewPolicy(domainId), cancellationToken);

            return true;
        }

        var properties = Writable(policy);

        if (PolicyDomains(properties).Any(id => Same(id, domainId)))
        {
            return false;
        }

        if (Associations(properties).FirstOrDefault() is { } first)
        {
            first["domains"] = References([.. Ids(first["domains"]), domainId]);
        }
        else
        {
            properties["parameters"] = NewPolicy(domainId)["properties"]!["parameters"]!.DeepClone();
        }

        await arm.PutAsync(PolicyPath, new JsonObject { ["properties"] = properties }, cancellationToken);

        return true;
    }

    private async Task<bool> LeavePolicyAsync(string domainId, CancellationToken cancellationToken)
    {
        if (await arm.GetAsync(PolicyPath, cancellationToken) is not { } policy)
        {
            return false;
        }

        var properties = Writable(policy);

        if (!PolicyDomains(properties).Any(id => Same(id, domainId)))
        {
            return false;
        }

        foreach (var association in Associations(properties))
        {
            association["domains"] = References([.. Ids(association["domains"]).Where(id => !Same(id, domainId))]);
        }

        if (PolicyDomains(properties).Count == 0)
        {
            await arm.DeleteAsync(PolicyPath, cancellationToken);

            return true;
        }

        await arm.PutAsync(PolicyPath, new JsonObject { ["properties"] = properties }, cancellationToken);

        return true;
    }

    // The template's route without the endpoint's own host name, which is the template's route's alone: two routes may
    // not answer the same domain, protocol and path (D-207).
    private JsonObject NewRoute(string domainId) => new()
    {
        ["properties"] = new JsonObject
        {
            ["customDomains"] = References([domainId]),
            ["originGroup"] = new JsonObject { ["id"] = $"{options.ProfileId}/originGroups/{options.OriginGroup}" },
            ["ruleSets"] = new JsonArray(new JsonObject { ["id"] = $"{options.ProfileId}/ruleSets/{options.RuleSet}" }),
            ["patternsToMatch"] = new JsonArray("/*"),
            ["supportedProtocols"] = new JsonArray("Http", "Https"),
            ["forwardingProtocol"] = "HttpsOnly",
            ["httpsRedirect"] = "Enabled",
            ["linkToDefaultDomain"] = "Disabled",
            ["enabledState"] = "Enabled",
        },
    };

    private JsonObject NewPolicy(string domainId) => new()
    {
        ["properties"] = new JsonObject
        {
            ["parameters"] = new JsonObject
            {
                ["type"] = "WebApplicationFirewall",
                ["wafPolicy"] = new JsonObject { ["id"] = options.WafPolicyId },
                ["associations"] = new JsonArray(new JsonObject
                {
                    ["domains"] = References([domainId]),
                    ["patternsToMatch"] = new JsonArray("/*"),
                }),
            },
        },
    };

    private string DomainPath(string hostName) => $"{options.ProfileId}/customDomains/{ResourceNameFor(hostName)}";

    private string DomainId(string hostName) => DomainPath(hostName);

    // What was read, without what only the service writes.
    private static JsonObject Writable(JsonObject resource)
    {
        var properties = (Properties(resource).DeepClone() as JsonObject)!;

        foreach (var name in ReadOnlyProperties)
        {
            properties.Remove(name);
        }

        return properties;
    }

    private static JsonObject Properties(JsonObject resource) => resource["properties"] as JsonObject ?? [];

    private static List<string> RouteDomains(JsonObject properties) => Ids(properties["customDomains"]);

    private static List<string> PolicyDomains(JsonObject properties) =>
        [.. Associations(properties).SelectMany(association => Ids(association["domains"]))];

    // The runtime's policy is made with one association, which a domain joins. One taken off is taken off wherever it
    // was listed.
    private static List<JsonObject> Associations(JsonObject properties) =>
        [.. (properties["parameters"]?["associations"] as JsonArray ?? []).OfType<JsonObject>()];

    private static List<string> Ids(JsonNode? references) =>
        [.. (references as JsonArray ?? []).OfType<JsonObject>().Select(reference => Text(reference, "id")).OfType<string>()];

    private static JsonArray References(IEnumerable<string> ids) => [.. ids.Select(id => (JsonNode)new JsonObject { ["id"] = id })];

    // Resource Manager hands ids back in whatever case it likes ("customdomains", "resourcegroups").
    private static bool Same(string left, string right) => string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    // Written and in place at the edge, or never sent there at all.
    private static bool Settled(JsonObject resource)
    {
        var properties = Properties(resource);

        return Text(properties, "provisioningState") == "Succeeded"
            && Text(properties, "deploymentStatus") is null or "NotStarted" or "Succeeded";
    }

    private static EdgeDomain Read(JsonObject resource)
    {
        var properties = Properties(resource);
        var validation = properties["validationProperties"] as JsonObject;

        return new EdgeDomain(
            Text(properties, "hostName") ?? string.Empty,
            Text(resource, "name") ?? string.Empty,
            Parse(Text(properties, "domainValidationState"), EdgeValidation.Unknown),
            Text(validation, "validationToken"),
            Text(validation, "expirationDate") is { } expires
                && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null,
            Parse(Text(properties, "provisioningState"), EdgeProvisioning.Creating),
            Parse(Text(properties, "deploymentStatus"), EdgeDeployment.NotStarted),
            Text(properties["tlsSettings"]?["secret"] as JsonObject, "id") is not null);
    }

    private static T Parse<T>(string? value, T otherwise)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : otherwise;

    // Empty is how Resource Manager says "none yet".
    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
