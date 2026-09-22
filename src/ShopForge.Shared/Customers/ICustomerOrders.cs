namespace ShopForge.Shared.Customers;

// Orders placed as a guest belong to the person who placed them: once they prove the e-mail address, their orders in
// this store are attached to the new account.
public interface ICustomerOrders
{
    Task<int> ClaimAsync(Guid storeCustomerId, string email, CancellationToken cancellationToken);
}
