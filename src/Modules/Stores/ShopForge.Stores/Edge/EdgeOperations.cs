using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Provisioning;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// A store's proved domains at the edge, and the zones the platform runs for them, as operations on the provisioning
// engine (D-210, D-212).
//
// Binding: the row the proof made; the custom domain at the edge, found or made; the validation record, published by
// the platform in a zone it runs and by the merchant otherwise; the edge's proof, waited for; the route and the security
// policy only the runtime edits; the certificate, waited for; and the record that sends traffic, last, so a domain
// already serving a site elsewhere does not move before it can be served here. Cancelled, it takes away only what it
// made. Unbinding takes everything off again, the traffic record first.
//
// A zone: made, found by name if it is already in the platform's account, waited for until the domain's nameservers
// point at it, and then used to prove the store's claims under it. The binding of what it proves is started by the pass
// that keeps bindings in step, so no step ever starts an operation of its own.
internal static class EdgeOperations
{
    public const string Bind = "domain.bind";
    public const string Unbind = "domain.unbind";
    public const string Revalidate = "domain.revalidate";
    public const string Zone = "domain.zone";

    // Long enough for a merchant to reach whoever runs their DNS, and for the edge's seven-day token to run out and be
    // replaced once.
    public static readonly TimeSpan ValidationGivesUpAfter = TimeSpan.FromDays(14);

    // The edge says minutes to an hour, occasionally longer.
    public static readonly TimeSpan CertificateGivesUpAfter = TimeSpan.FromDays(1);

    public static readonly TimeSpan DelegationGivesUpAfter = TimeSpan.FromDays(14);

    // A zone on Cloudflare's free plan may ask for a look at its delegation once an hour.
    public static readonly TimeSpan DelegationCheckEvery = TimeSpan.FromHours(1);

    // A replaced token is read within a minute: the shortest a record may live on the free plan.
    public const int ProofTtl = 60;

    // Cloudflare's "automatic".
    public const int TrafficTtl = 1;

    public static string ZoneSubject(string zoneName) => $"zone:{zoneName}";

    // Says whose record it is. A comment on the free plan holds a hundred characters.
    public static string Comment(Guid operationId) => $"ShopForge, operation {operationId:N}";

    public static DnsRecordSpec Proof(string name, string value, Guid operationId) => new(name, "TXT", value, ProofTtl, Comment(operationId));

    public static DnsRecordSpec Traffic(string hostName, string target, Guid operationId) =>
        new(hostName, "CNAME", target, TrafficTtl, Comment(operationId));

    // The binding of a host in the current store.
    public static Task<DomainBinding?> BindingAsync(DbContext dbContext, string? hostName, CancellationToken cancellationToken) =>
        dbContext.Set<DomainBinding>().SingleOrDefaultAsync(binding => binding.HostName == hostName, cancellationToken);

    public static Task<StoreZone?> StoreZoneAsync(DbContext dbContext, string zoneName, CancellationToken cancellationToken) =>
        dbContext.Set<StoreZone>().SingleOrDefaultAsync(zone => zone.Name == zoneName, cancellationToken);

    public static string ZoneNameOf(string subject) =>
        subject.StartsWith("zone:", StringComparison.Ordinal) ? subject["zone:".Length..] : subject;

    // A provider that asked to be left alone is waited out, for as long as it said, using up no tries; one that refused
    // what it would refuse again stops the operation for a person. Anything else is the engine's to try again (D-210).
    public static async Task<StepOutcome> GuardAsync(Func<Task<StepOutcome>> step)
    {
        try
        {
            return await step();
        }
        catch (ProviderException throttled) when (throttled.IsThrottled)
        {
            return StepOutcome.Waiting($"{throttled.Provider} asked to be left alone for a while.", throttled.RetryAfter);
        }
        catch (ProviderException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }

    public static bool GaveUp(IStepContext context, TimeSpan after, DateTimeOffset now) =>
        context.WaitingSince is { } since && now - since > after;
}

internal sealed class BindDomain : IOperationKind
{
    public string Name => EdgeOperations.Bind;

    public IReadOnlyList<string> Steps => ["register", "edge-domain", "validation-record", "validation", "routing", "certificate", "traffic", "activate"];
}

internal sealed class UnbindDomain : IOperationKind
{
    public string Name => EdgeOperations.Unbind;

    public IReadOnlyList<string> Steps => ["traffic", "routing", "edge-domain", "validation-record", "forget"];
}

// The edge wants the domain proved again before it renews the certificate: an apex served through a flattened CNAME,
// or a token that ran out or was refused.
internal sealed class RevalidateDomain : IOperationKind
{
    public string Name => EdgeOperations.Revalidate;

    public IReadOnlyList<string> Steps => ["validation"];
}

internal sealed class RunZone : IOperationKind
{
    public string Name => EdgeOperations.Zone;

    public IReadOnlyList<string> Steps => ["register", "zone", "delegation", "proof"];
}
