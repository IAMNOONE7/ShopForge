namespace ShopForge.Shared.Customers;

// The storefront customer behind the current request, if the visitor is signed in. Storefront endpoints are open to
// guests, so the customer session is read on demand rather than by the authentication middleware.
public interface ICurrentCustomer
{
    Task<Guid?> FindStoreCustomerIdAsync(CancellationToken cancellationToken);
}
