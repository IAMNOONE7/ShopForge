using System.Collections.Concurrent;

namespace ShopForge.TestRig.Runs;

// The runs this rig has started. A run is a company of its own with two published stores, an owner, a Comgate
// merchant and a small catalogue, and the tool shop if it was asked for; everything a journey reads or controls is reached through its id, so two runs on one
// stack never touch each other's data (D-203).
internal sealed class RunBook
{
    private readonly ConcurrentDictionary<string, Run> _runs = new(StringComparer.Ordinal);

    public void Add(Run run) => _runs[run.Id] = run;

    public Run? Find(string id) => _runs.GetValueOrDefault(id);

    public void Forget(string id) => _runs.TryRemove(id, out _);
}

// What a journey needs to know about its run. The owner's password and the operator's are the run's own, made for it;
// the merchant's Comgate secret never leaves the rig. A run asked for with the tool shop has it as a third store of its
// company, apart from the two every run has (D-227).
internal sealed record Run(
    string Id,
    string EmailDomain,
    Guid TenantId,
    Person Owner,
    ComgateAccount Comgate,
    IReadOnlyList<RunStore> Stores,
    IReadOnlyList<RunProduct> Products,
    RunStore? ToolShop,
    DateTimeOffset StartedAt)
{
    public bool Owns(Guid storeId) => Stores.Any(store => store.Id == storeId) || ToolShop?.Id == storeId;
}

internal sealed record Person(string Email, string Password);

internal sealed record ComgateAccount(string Merchant);

internal sealed record RunStore(Guid Id, string Name, string Host);

internal sealed record RunProduct(Guid Id, string Name, string Slug, decimal Price, IReadOnlyList<RunVariant> Variants, IReadOnlyDictionary<Guid, Guid> Listings);

internal sealed record RunVariant(Guid Id, string Sku, string? Size, int Stock);
