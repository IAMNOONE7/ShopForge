namespace ShopForge.Shared.Edge;

// Merchants' domains at the edge (D-207, D-212): a custom domain whose certificate the edge issues and renews once a TXT
// record proves the name, and a place on the route and the security policy only the runtime edits. The template owns
// the platform's own domains, its route and its policy, and nothing here writes to them.
public interface IEdgeDomains
{
    // Whether this deployment binds domains at the edge at all. Development and most tests do not.
    bool IsConfigured { get; }

    // What a domain's CNAME record points at: the endpoint's own host name.
    string TrafficTarget { get; }

    Task<EdgeDomain?> FindAsync(string hostName, CancellationToken cancellationToken);

    // With a certificate the edge manages, and TLS 1.2 at least.
    Task<EdgeDomain> CreateAsync(string hostName, CancellationToken cancellationToken);

    // A new validation token, for one that expired, was refused, or is needed again before a certificate is renewed. Any
    // domain of the profile's, the template's own included.
    Task RefreshValidationAsync(EdgeDomain domain, CancellationToken cancellationToken);

    // Gone already is gone.
    Task DeleteAsync(string hostName, CancellationToken cancellationToken);

    // Puts the domain on the runtime's route and security policy, making either the first time. Writes nothing when it
    // is on both already, and answers whether it wrote.
    Task<bool> JoinAsync(string hostName, CancellationToken cancellationToken);

    // Takes it off both, and deletes either it was the last domain on, so neither is ever left with none.
    Task<bool> LeaveAsync(string hostName, CancellationToken cancellationToken);

    Task<EdgeMembership> MembershipAsync(string hostName, CancellationToken cancellationToken);

    // Every custom domain in the profile, the template's included, for the pass that keeps validations current.
    Task<IReadOnlyList<EdgeDomain>> ListAsync(CancellationToken cancellationToken);
}

public sealed record EdgeDomain(
    string HostName,
    string ResourceName,
    EdgeValidation Validation,
    string? ValidationToken,
    DateTimeOffset? ValidationTokenExpires,
    EdgeProvisioning Provisioning,
    EdgeDeployment Deployment,
    bool CertificateIssued)
{
    // A domain the runtime made for a merchant, as against one of the template's.
    public bool IsMerchants => EdgeNames.IsMerchants(ResourceName);

    // Proved, its certificate issued, and nothing of it still on its way to the edge's locations. A deployment the edge
    // never starts counts as done: the reference does not say a domain's always runs, and V19 will (UNVERIFIED).
    public bool IsReady =>
        Validation == EdgeValidation.Approved
        && CertificateIssued
        && Provisioning == EdgeProvisioning.Succeeded
        && Deployment is EdgeDeployment.NotStarted or EdgeDeployment.Succeeded;

    public string ValidationRecordName => EdgeNames.ValidationRecord(HostName);

    // Whether the token can still be used: present, and not past the date the edge gave it.
    public bool HasLiveToken(DateTimeOffset now) =>
        !string.IsNullOrEmpty(ValidationToken) && (ValidationTokenExpires is not { } expires || expires > now);

    // The edge has stopped trusting the proof it had, or never got one in time, and wants a new token published.
    public bool NeedsNewProof(DateTimeOffset now) =>
        Validation is EdgeValidation.TimedOut or EdgeValidation.Rejected or EdgeValidation.InternalError
        || (Validation is EdgeValidation.Pending or EdgeValidation.PendingRevalidation && !HasLiveToken(now));
}

// The edge's own words for how far the proof of a domain has got.
public enum EdgeValidation
{
    Unknown,
    Submitting,
    Pending,
    Rejected,
    TimedOut,
    PendingRevalidation,
    Approved,
    RefreshingValidationToken,
    InternalError,
}

public enum EdgeProvisioning
{
    Creating,
    Updating,
    Deleting,
    Succeeded,
    Failed,
}

public enum EdgeDeployment
{
    NotStarted,
    InProgress,
    Succeeded,
    Failed,
}

// Whether the domain is on the route and the policy, and whether the edge has finished putting both in place.
public sealed record EdgeMembership(bool OnRoute, bool OnPolicy, bool Settled);

public static class EdgeNames
{
    // The start of every custom domain name the runtime gives a merchant's domain, so a domain of the template's is
    // never mistaken for one.
    public const string MerchantPrefix = "m-";

    // The name the edge reads the proof at: `_dnsauth.<host>`, and for a wildcard the name it stands under. That last
    // is the apex rule applied to the wildcard's stem, which the edge's documentation does not spell out: UNVERIFIED
    // until a real wildcard is validated (V19).
    public static string ValidationRecord(string hostName) =>
        $"_dnsauth.{(hostName.StartsWith("*.", StringComparison.Ordinal) ? hostName[2..] : hostName)}";

    public static bool IsMerchants(string resourceName) => resourceName.StartsWith(MerchantPrefix, StringComparison.Ordinal);
}
