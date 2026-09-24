namespace ShopForge.Orders.Domain;

// A discount is taken off the lines, never off the total: prices are gross and each line has its own VAT rate, so
// the only way the VAT summary can stay right is to know what was charged for each line (D-085).
internal static class DiscountAllocation
{
    public static DiscountResult For(Discount discount, IReadOnlyList<decimal> lineTotals, decimal shippingPrice)
    {
        if (discount.Kind == DiscountKind.FreeShipping)
        {
            return new DiscountResult([.. lineTotals.Select(_ => 0m)], shippingPrice);
        }

        var itemsTotal = lineTotals.Sum();
        var amount = discount.Kind == DiscountKind.Percentage
            ? decimal.Round(itemsTotal * discount.Value / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Min(discount.Value, itemsTotal);

        return new DiscountResult(Spread(amount, lineTotals), 0m);
    }

    // Each line gets its share rounded down to the currency's two decimals, never more than the line costs. That
    // leaves a few hundredths over, which are handed to the lines that still have room, biggest first, until the
    // parts add up to the discount exactly rather than approximately.
    private static IReadOnlyList<decimal> Spread(decimal amount, IReadOnlyList<decimal> lineTotals)
    {
        var itemsTotal = lineTotals.Sum();

        if (amount <= 0 || itemsTotal <= 0)
        {
            return [.. lineTotals.Select(_ => 0m)];
        }

        var shares = lineTotals
            .Select(lineTotal => Math.Min(lineTotal, decimal.Round(amount * lineTotal / itemsTotal, 2, MidpointRounding.ToZero)))
            .ToList();

        var remainder = amount - shares.Sum();

        foreach (var (_, index) in lineTotals.Select((lineTotal, index) => (lineTotal, index)).OrderByDescending(line => line.lineTotal))
        {
            if (remainder <= 0)
            {
                break;
            }

            var room = Math.Min(remainder, lineTotals[index] - shares[index]);
            shares[index] += room;
            remainder -= room;
        }

        return shares;
    }
}

internal sealed record DiscountResult(IReadOnlyList<decimal> LineDiscounts, decimal ShippingDiscount)
{
    public decimal Total => LineDiscounts.Sum() + ShippingDiscount;
}
