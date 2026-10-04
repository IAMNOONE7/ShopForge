namespace ShopForge.Shared.Connections;

// Which merchant account a store takes money through. One company may run several storefronts, each approved for
// its own connection, and a payment for one must never travel through another's (D-138).
//
// A connection is not an account. Stores of one legal entity may share an account while being told apart by what
// they send under, so the connection holds what distinguishes the store even when the credentials behind it do
// not — and because it holds only the *name* of the secret, there is nothing here that could be returned to a
// browser by accident (D-139).
public interface IProviderConnections
{
    Task<ProviderConnection?> FindAsync(string provider, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ConnectedAsync(CancellationToken cancellationToken);
}

public sealed record ProviderConnection(string Provider, string MerchantId, ProviderEnvironment Environment, string? SecretName)
{
    // A connection with nowhere to read its credentials from cannot be used, and saying so here keeps every
    // caller from having to remember it.
    public bool IsUsable => SecretName is { Length: > 0 };
}

// Set deliberately on the connection and switchable by neither a shopper nor an order (D-143). A deployment
// serving real customers uses live connections; test ones belong in development and staging.
public enum ProviderEnvironment
{
    Test,
    Live,
}
