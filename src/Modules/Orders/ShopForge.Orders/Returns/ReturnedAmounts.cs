using ShopForge.Shared.Payments;

namespace ShopForge.Orders.Returns;

// A line bought as a whole can come back in pieces, so its discount has to come back in pieces too. Working from
// the running total rather than from each piece means the shares always add up to the discount, however the line
// is split and in whatever order the parcels arrive (D-096).
internal static class ReturnedAmounts
{
    public static decimal DiscountShare(decimal discount, int quantity, int alreadyReturned, int returning, Currency currency) =>
        Portion(discount, alreadyReturned + returning, quantity, currency) - Portion(discount, alreadyReturned, quantity, currency);

    private static decimal Portion(decimal discount, int quantity, int of, Currency currency) =>
        currency.Round(discount * quantity / of);
}
