namespace ShopForge.Shared.Shipping;

// What a shop charges to deliver, for the benefit of anything that has to publish it. A shopping feed states
// shipping per country and per service, and a shop can only state what it actually knows (D-169).
public interface IStoreShippingRates
{
    Task<IReadOnlyList<ShippingRate>> FindAsync(CancellationToken cancellationToken);
}

// One flat price for one named service in one country. A method that has not said which countries it serves
// produces none of these: a guess here becomes a promise somebody else publishes.
public sealed record ShippingRate(string Country, string Service, decimal Price);
