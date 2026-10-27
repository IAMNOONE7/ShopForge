using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Shipping;

// What a cart costs to send by one method, or why that method cannot take it, answered in one place (D-219): the
// merchant's preview asks it now and the checkout asks the same thing from V28f. A flat method answers its one price
// and its limits (D-145). A method priced by the parcel estimates the parcels (D-217) within its service and its own
// limits (D-218), and prices each by its rate card. Nothing is charged but the parcels (decision 10).
//
// It reads the method's service and the store's packaging and nothing per item, so a cart of any length costs the same
// few queries.
internal sealed class ShippingQuotes(DbContext dbContext, ICurrentStoreSettings storeSettings, IEnumerable<IShippingProvider> providers)
{
    public async Task<ShippingQuote> QuoteAsync(
        ShippingMethod method,
        IReadOnlyList<QuotedItem> items,
        string? destinationCountry,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var destination = string.IsNullOrWhiteSpace(destinationCountry) ? null : destinationCountry.Trim().ToUpperInvariant();
        ShippingQuote quote;

        if (method.Pricing == ShippingPricing.Flat)
        {
            quote = Quoting.Flat(method, items, destination, currency);
        }
        else
        {
            var service = await dbContext.Set<CarrierService>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Key == method.CarrierServiceKey, cancellationToken);
            var packing = (await dbContext.Set<ShippingSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.Packing ?? ShippingSettings.DefaultPacking;
            quote = Quoting.PerParcel(method, service, packing, items, destination, currency);
        }

        return quote.Unavailable is null ? await AskTheCarrierAsync(method, quote, items, destination, currency, cancellationToken) : quote;
    }

    // What a carrier is told about the parcel when a point is checked or its map opened (D-220). For a method priced by
    // the parcel, the heaviest parcel estimated, and the least any parcel could measure that holds the cart's largest
    // things, which is each sorted side at its longest across the cart: a box that cannot take that cannot take the
    // order. A flat method tells it no more than before.
    public static Parcel ForPoint(ShippingQuote quote, IReadOnlyList<QuotedItem> items, string? destination)
    {
        var sides = items
            .Where(item => item.Variant is { LengthMm: not null, WidthMm: not null, HeightMm: not null })
            .Select(item => Sides.Of(item.Variant.LengthMm!.Value, item.Variant.WidthMm!.Value, item.Variant.HeightMm!.Value))
            .ToList();

        return quote.Parcels.Count == 0 || sides.Count == 0
            ? new Parcel(null, destination)
            : new Parcel(
                quote.Parcels.Max(parcel => parcel.GrossWeightGrams),
                destination,
                sides.Max(side => side.Longest),
                sides.Max(side => side.Middle),
                sides.Max(side => side.Shortest));
    }

    // What only the carrier can say at the time (D-145), asked about every parcel the rules let through. A flat
    // method's cart is one parcel.
    private async Task<ShippingQuote> AskTheCarrierAsync(
        ShippingMethod method,
        ShippingQuote quote,
        IReadOnlyList<QuotedItem> items,
        string? destination,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var provider = providers.SingleOrDefault(candidate => candidate.Key == method.ProviderKey);
        IReadOnlyList<Parcel> parcels = quote.Parcels.Count == 0
            ? [new Parcel(Quoting.WeightOf(items), destination)]
            : [.. quote.Parcels.Select(parcel => new Parcel(parcel.GrossWeightGrams, destination))];

        foreach (var parcel in parcels)
        {
            if (provider is null || !await provider.CanCarryAsync(parcel, cancellationToken))
            {
                return ShippingQuote.Refused(currency, QuoteRefusalReason.CarrierRefused, "The carrier will not take it.");
            }
        }

        return quote;
    }
}

// The rules, with nothing to read: the same method, items and settings always give the same quote.
internal static class Quoting
{
    public static ShippingQuote Flat(ShippingMethod method, IReadOnlyList<QuotedItem> items, string? destination, Currency currency)
    {
        if (destination is not null && method.Countries.Count > 0 && !method.Countries.Contains(destination))
        {
            return ShippingQuote.Refused(currency, QuoteRefusalReason.NotServed, $"This method does not deliver to {destination}.");
        }

        // An unknown weight is not a light one: a limit cannot be promised about what nobody weighed (D-145).
        if (method.MaxWeightGrams is { } limit)
        {
            if (WeightOf(items) is not { } weight)
            {
                var unweighed = items.FirstOrDefault(item => item.Variant.WeightGrams is null);

                return ShippingQuote.Refused(
                    currency,
                    QuoteRefusalReason.MissingWeight,
                    unweighed is null
                        ? $"Nothing in the order is weighed, and this method takes up to {limit} g."
                        : $"{unweighed.Variant.Sku} has no weight, and this method takes up to {limit} g.",
                    unweighed?.Variant.Id);
            }

            if (weight > limit)
            {
                return ShippingQuote.Refused(currency, QuoteRefusalReason.TooHeavy, $"The order weighs {weight} g; this method takes up to {limit} g.");
            }
        }

        return new ShippingQuote(currency.Code, method.Price, [], null);
    }

    public static ShippingQuote PerParcel(
        ShippingMethod method,
        CarrierService? service,
        ParcelPacking packing,
        IReadOnlyList<QuotedItem> items,
        string? destination,
        Currency currency)
    {
        if (service is not { IsActive: true } || service.ProviderKey != method.ProviderKey)
        {
            return ShippingQuote.Refused(currency, QuoteRefusalReason.ServiceUnavailable, "The carrier service this method sends by is not in use.");
        }

        if (destination is not null && destination != service.Country)
        {
            return ShippingQuote.Refused(currency, QuoteRefusalReason.NotServed, $"{service.Name} delivers only to {service.Country}.");
        }

        if (method.RateBands.Count == 0)
        {
            return ShippingQuote.Refused(currency, QuoteRefusalReason.NoRates, "This method has no prices yet.");
        }

        var limits = method.LimitsWithin(service);
        var estimate = ParcelEstimator.Estimate(
            [.. items.Select(item => new ParcelItem(item.Variant.Id, item.Quantity, item.Variant.WeightGrams, item.Variant.LengthMm, item.Variant.WidthMm, item.Variant.HeightMm, item.Variant.ShipsAlone))],
            limits,
            packing);
        var skus = items.GroupBy(item => item.Variant.Id).ToDictionary(group => group.Key, group => group.First().Variant.Sku);

        if (estimate.Refusal is { } refusal)
        {
            var sku = refusal.VariantId is { } id ? skus[id] : null;
            var most = Math.Min(ParcelEstimator.MostParcels, Math.Min(packing.MostParcels, limits.MaxParcels ?? int.MaxValue));

            return ShippingQuote.Refused(
                currency,
                Enum.Parse<QuoteRefusalReason>(refusal.Reason.ToString()),
                refusal.Reason switch
                {
                    ParcelRefusalReason.MissingWeight => $"{sku} has no weight, so a parcel holding it cannot be priced.",
                    ParcelRefusalReason.MissingSize => $"{sku} has no length, width and height, so a parcel holding it cannot be priced.",
                    ParcelRefusalReason.ItemTooLarge => $"{sku} is larger than {service.Name} takes.",
                    ParcelRefusalReason.ItemTooHeavy => $"{sku} in its packaging is heavier than {service.Name} takes.",
                    _ => $"The order would need more than {most} {(most == 1 ? "parcel" : "parcels")}, more than is sent by parcel.",
                },
                refusal.VariantId);
        }

        var parcels = new List<QuotedParcel>();

        foreach (var parcel in estimate.Parcels)
        {
            if (method.BandFor(parcel.GrossWeightGrams) is not { } band)
            {
                return ShippingQuote.Refused(
                    currency,
                    QuoteRefusalReason.AboveHighestBand,
                    $"A parcel would weigh {parcel.GrossWeightGrams} g, more than this method's heaviest band, up to {method.RateBands.Max(rate => rate.UpToGrams)} g.");
            }

            parcels.Add(new QuotedParcel(
                [.. parcel.Contents.Select(content => new QuotedContent(content.VariantId, skus[content.VariantId], content.Quantity))],
                parcel.GrossWeightGrams,
                band.UpToGrams,
                band.Price,
                parcel.Alone));
        }

        return new ShippingQuote(currency.Code, parcels.Sum(parcel => parcel.Price), parcels, null);
    }

    // What the whole cart weighs, or nothing when any of it is unweighed or there is nothing in it to weigh, as a
    // checkout without a cart has always been asked (D-145).
    public static int? WeightOf(IReadOnlyList<QuotedItem> items) =>
        items.Count == 0 || items.Any(item => item.Variant.WeightGrams is null)
            ? null
            : (int)Math.Min(int.MaxValue, items.Sum(item => (long)item.Variant.WeightGrams!.Value * item.Quantity));
}

internal sealed record QuotedItem(SellableVariant Variant, int Quantity);

// The total is the parcels' prices added up, each already in the currency's own decimals, so nothing is rounded.
internal sealed record ShippingQuote(string Currency, decimal Total, IReadOnlyList<QuotedParcel> Parcels, QuoteRefusal? Unavailable)
{
    public bool IsAvailable => Unavailable is null;

    public static ShippingQuote Refused(Currency currency, QuoteRefusalReason reason, string detail, Guid? variantId = null) =>
        new(currency.Code, 0m, [], new QuoteRefusal(reason, detail, variantId));
}

internal sealed record QuotedParcel(IReadOnlyList<QuotedContent> Contents, int GrossWeightGrams, int BandUpToGrams, decimal Price, bool Alone);

internal sealed record QuotedContent(Guid VariantId, string Sku, int Quantity);

internal sealed record QuoteRefusal(QuoteRefusalReason Reason, string Detail, Guid? VariantId);

internal enum QuoteRefusalReason
{
    NotServed,
    TooHeavy,
    MissingWeight,
    MissingSize,
    ItemTooLarge,
    ItemTooHeavy,
    TooManyParcels,
    AboveHighestBand,
    NoRates,
    ServiceUnavailable,
    CarrierRefused,
}
