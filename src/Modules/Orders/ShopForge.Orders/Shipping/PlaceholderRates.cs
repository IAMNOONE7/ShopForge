using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.Orders.Shipping;

// Invented round numbers, not Packeta's, on the owner's word (2026-10-24): development, the tests and a first
// configuration have prices to work with until the merchant's price list arrives, and readiness will not let a store
// open while a method offers them (D-219). They are korunas, so a store in another currency starts with no prices.
internal static class PlaceholderRates
{
    public static IReadOnlyList<RateBand>? For(string? carrierServiceKey, Currency currency) =>
        currency.Code != "CZK"
            ? null
            : carrierServiceKey switch
            {
                "packeta-cz-zbox" => [new(2_000, 79m), new(5_000, 99m), new(10_000, 129m), new(15_000, 159m)],
                "packeta-cz-home" => [new(2_000, 109m), new(5_000, 129m)],
                _ => null,
            };

    // A method that has just come to be priced by the parcel, or moved to another service, starts from that service's
    // placeholder prices, unless the prices it has are the merchant's own.
    public static void StartFrom(ShippingMethod method, Currency currency)
    {
        if (method.RatesFrom is RateSource.Carrier or RateSource.Own)
        {
            return;
        }

        if (For(method.CarrierServiceKey, currency) is { } bands)
        {
            method.PriceParcels(RateSource.Placeholder, null, bands, currency);
        }
        else
        {
            method.ForgetRates();
        }
    }
}
