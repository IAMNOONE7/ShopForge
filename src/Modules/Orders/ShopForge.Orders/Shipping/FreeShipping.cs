namespace ShopForge.Orders.Shipping;

// Delivery costs nothing once the goods come to the store's threshold (D-221). The goods are counted as the shopper
// pays for them, gross and after any code's share, and exactly the threshold reaches it. No threshold is a store with
// free delivery switched off.
internal static class FreeShipping
{
    public static bool Applies(decimal? threshold, decimal goods) => threshold is { } reached && goods >= reached;

    // How much more in goods would make delivery free: nothing once it is, and no answer while it is off.
    public static decimal? Remaining(decimal? threshold, decimal goods) =>
        threshold is { } reached ? Math.Max(0m, reached - goods) : null;

    // The quote keeps what delivery costs and adds what the threshold takes off, so the order can keep both, as it keeps
    // a free-shipping code's (D-221). A method that costs nothing is left as it is, and so is one that cannot take the
    // cart, whose quote costs nothing.
    public static ShippingQuote Apply(ShippingQuote quote, decimal? threshold, decimal? goods) =>
        quote.Total > 0 && goods is { } paid && Applies(threshold, paid)
            ? quote with { ShippingDiscount = quote.Total, FreeShippingThreshold = threshold }
            : quote;
}
