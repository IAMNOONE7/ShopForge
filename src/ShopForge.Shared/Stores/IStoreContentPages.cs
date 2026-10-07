namespace ShopForge.Shared.Stores;

// What a shop has published in its own words, for whoever needs to list it without owning it — a sitemap
// belongs to the half of the system that knows about crawlers, and the pages belong to the store (D-175).
public interface IStoreContentPages
{
    Task<IReadOnlyList<string>> AdvertisedSlugsAsync(CancellationToken cancellationToken);
}
