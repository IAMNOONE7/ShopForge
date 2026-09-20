using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Publishing;

internal sealed class OrdersPublishCheck(DbContext dbContext) : IStorePublishCheck
{
    public async Task<string?> FindProblemAsync(CancellationToken cancellationToken)
    {
        var hasPayment = await dbContext.Set<PaymentMethod>().AnyAsync(method => method.IsActive, cancellationToken);
        var hasShipping = await dbContext.Set<ShippingMethod>().AnyAsync(method => method.IsActive, cancellationToken);

        return (hasPayment, hasShipping) switch
        {
            (false, false) => "The store has no active payment or shipping method.",
            (false, _) => "The store has no active payment method.",
            (_, false) => "The store has no active shipping method.",
            _ => null,
        };
    }
}
