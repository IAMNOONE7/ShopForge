namespace ShopForge.Shared.Stores;

// Each module answers whether the current store is ready to go live. A module may report several things, because
// "payments are switched on" and "payments are connected to an account" are different facts and a merchant told
// only the first one has a shop that cannot take money (D-190).
public interface IStorePublishCheck
{
    Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken);
}

// One fact about a store, named so a screen can translate it and an operator can grep for it. The name is the
// contract; the problem is prose and may be reworded.
public sealed record StoreReadiness(string Check, bool IsReady, string? Problem, ReadinessWeight Weight)
{
    public static StoreReadiness Ready(string check, ReadinessWeight weight = ReadinessWeight.Required) =>
        new(check, true, null, weight);

    public static StoreReadiness NotReady(string check, string problem, ReadinessWeight weight = ReadinessWeight.Required) =>
        new(check, false, problem, weight);

    // What stands between a store and being published. Advice does not.
    public bool Blocks => !IsReady && Weight == ReadinessWeight.Required;
}

public enum ReadinessWeight
{
    // A shop that fails this cannot serve a customer, so publication is refused.
    Required,

    // Worth telling the merchant and not worth stopping them: something is configured in a way that probably
    // does not do what they think, or something belongs to the operator rather than to them.
    Advised,
}
