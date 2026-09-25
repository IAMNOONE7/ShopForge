using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Storefront;

internal sealed class CustomerPurchases(DbContext dbContext) : ICustomerPurchases
{
    // Paid or shipped: an order that is still awaiting payment, cancelled or refunded is not a purchase.
    public Task<bool> HasBoughtAsync(Guid storeCustomerId, Guid storeProductId, CancellationToken cancellationToken) =>
        dbContext.Set<Order>()
            .AnyAsync(
                order => order.StoreCustomerId == storeCustomerId
                    && (order.Status == OrderStatus.Paid || order.Status == OrderStatus.Shipped)
                    && order.Lines.Any(line => line.StoreProductId == storeProductId),
                cancellationToken);
}

internal sealed class OrderUsage(DbContext dbContext) : ITenantUsage
{
    public async Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
    [
        new UsageCount("orders", await dbContext.Set<Order>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .CountAsync(
                order => storeIds.Contains(order.StoreId)
                    && order.Status != OrderStatus.AwaitingPayment
                    && order.Status != OrderStatus.Cancelled,
                cancellationToken)),
    ];
}
