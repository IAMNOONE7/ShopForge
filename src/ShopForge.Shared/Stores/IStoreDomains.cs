namespace ShopForge.Shared.Stores;

// The host names the current store has proved it owns, as against the one the platform gave it (D-124, D-189). Mail
// is only ever sent from under one of these, or a store could take somebody else's domain into the platform's mail
// account and keep its real owner out (D-211).
public interface IStoreDomains
{
    Task<IReadOnlyList<string>> ProvedAsync(CancellationToken cancellationToken);
}
