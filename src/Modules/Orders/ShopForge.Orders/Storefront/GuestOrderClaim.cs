using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Customers;

namespace ShopForge.Orders.Storefront;

internal sealed class GuestOrderClaim(DbContext dbContext) : ICustomerOrders
{
    public Task<int> ClaimAsync(Guid storeCustomerId, string email, CancellationToken cancellationToken) =>
        dbContext.Set<Order>()
            .Where(order => order.StoreCustomerId == null && order.Email == email)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.StoreCustomerId, storeCustomerId), cancellationToken);
}
