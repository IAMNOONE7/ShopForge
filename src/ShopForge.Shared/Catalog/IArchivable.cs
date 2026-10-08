namespace ShopForge.Shared.Catalog;

// A row a merchant has retired: out of the shop and out of their own working list, still there to be read and
// to be restored. The filter that hides it is applied once for everything that can be retired, so a listing
// that has been archived disappears from the catalogue, the feeds, the sitemap and the search without any of
// them being told about archiving (D-180).
public interface IArchivable
{
    DateTimeOffset? ArchivedAt { get; }
}
