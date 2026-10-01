namespace ShopForge.Shared.Payments;

// A gateway's callback names the merchant account it concerns, because that is the only party the gateway knows
// about; which storefront sells under that account is ours to answer. This is therefore one of the few questions
// asked across stores, and its answer is what scopes everything after it (D-141).
//
// It is kept apart from IProviderConnections on purpose: that one answers for the store in scope and takes no
// store id, which is exactly the property that makes a credential safe to resolve through it.
public interface IMerchantConnections
{
    // Usually one store, occasionally several: stores of one company may be approved under a single account.
    Task<IReadOnlyList<Guid>> StoresAsync(string provider, string merchantId, CancellationToken cancellationToken);
}
