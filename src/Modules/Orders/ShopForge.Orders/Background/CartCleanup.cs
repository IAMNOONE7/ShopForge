using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Orders.Background;

// A cart is a cookie away from being forgotten; rows for shoppers who never came back should not pile up forever.
internal sealed class CartCleanup(DbContext dbContext, TimeProvider clock) : IStoreMaintenance
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public string Name => "Abandoned carts";

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Lifetime;

        return dbContext.Set<Cart>().Where(cart => cart.UpdatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
