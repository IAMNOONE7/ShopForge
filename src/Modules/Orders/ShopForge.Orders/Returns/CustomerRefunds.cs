using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Returns;

// What a shopper may know about money on its way back: how much was credited, whether it is confirmed back with them,
// and when. Never why it is not — a gateway's refusal and a merchant's note about settling it are the store's
// business, and a refused refund the store is putting right is, to the shopper, one still in progress (D-204).
internal static class CustomerRefunds
{
    public const string Settled = "Settled";
    public const string InProgress = "InProgress";

    // Paid by a method the store settles itself, so ShopForge cannot say when the money arrives (D-081).
    public const string ByStore = "ByStore";

    public static string? StateOf(OrderReturn orderReturn) => orderReturn.Refund switch
    {
        null => null,
        RefundStatus.Refunded or RefundStatus.ClosedByHand => Settled,
        RefundStatus.ByStore => ByStore,
        _ => InProgress,
    };

    // A refund tried again keeps the time of the attempt before it, so the time is read only for one that is settled.
    public static DateTimeOffset? SettledAtOf(OrderReturn orderReturn) =>
        StateOf(orderReturn) == Settled ? orderReturn.RefundSettledAt : null;

    // Every refund of an order the shopper can see, the store's own outright refunds included: a guest has no returns
    // to look at, and an order refunded whole is still money owed to them.
    public static async Task<List<CustomerRefundResponse>> OfOrderAsync(DbContext dbContext, string orderNumber, CancellationToken cancellationToken) =>
        [.. (await dbContext.Set<OrderReturn>()
                .AsNoTracking()
                .Where(orderReturn => orderReturn.OrderNumber == orderNumber && orderReturn.Refund != null)
                .OrderBy(orderReturn => orderReturn.ReceivedAt)
                .ToListAsync(cancellationToken))
            .Select(orderReturn => new CustomerRefundResponse(orderReturn.Number, orderReturn.RefundedAmount, StateOf(orderReturn)!, SettledAtOf(orderReturn)))];
}

internal sealed record CustomerRefundResponse(string ReturnNumber, decimal Credited, string State, DateTimeOffset? SettledAt);
