namespace ShopForge.Shared.Shipping;

// What delivery costs at the least in the current store, so a shopper sees it before the checkout (D-220).
public interface IStoreDeliveryPrices
{
    // The cheapest method on offer: a flat method's price, or the lowest band of one priced by the parcel. Null when the
    // store offers no method a shopper could choose.
    Task<decimal?> CheapestAsync(CancellationToken cancellationToken);
}
