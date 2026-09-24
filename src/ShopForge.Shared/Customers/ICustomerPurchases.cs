namespace ShopForge.Shared.Customers;

// Whether a customer actually bought something, which is what a review is allowed to be written about (D-088).
public interface ICustomerPurchases
{
    Task<bool> HasBoughtAsync(Guid storeCustomerId, Guid storeProductId, CancellationToken cancellationToken);
}
