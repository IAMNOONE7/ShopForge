namespace ShopForge.Orders.Returns;

// A line bought as a whole can come back in pieces, so its discount has to come back in pieces too. Working from
// the running total rather than from each piece means the shares always add up to the discount, however the line
// is split and in whatever order the parcels arrive (D-096).
internal static class ReturnedAmounts
{
    public static decimal DiscountShare(decimal discount, int quantity, int alreadyReturned, int returning) =>
        Portion(discount, alreadyReturned + returning, quantity) - Portion(discount, alreadyReturned, quantity);

    private static decimal Portion(decimal discount, int quantity, int of) =>
        decimal.Round(discount * quantity / of, 2, MidpointRounding.AwayFromZero);
}
