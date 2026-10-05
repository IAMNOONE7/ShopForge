using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests.Stores;

// Everything that has to name a page of a shop — a sitemap, a feed, a canonical tag, an e-mail — names the
// same address for it, and that address is the shop's own (D-164).
public sealed class StoreUrlTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The shapes the storefront serves. Its router carries the matching routes and a test there says so:
    // changing one side alone is how a sitemap starts advertising pages that do not exist.
    [Fact]
    public async Task Every_kind_of_page_has_one_address_on_the_stores_own_host()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var address = await AddressAsync(store);

        Assert.NotNull(address);
        Assert.Equal($"https://{store.HostName}/", address.Home);
        Assert.Equal($"https://{store.HostName}/p/oak-chair", address.Product("oak-chair"));
        Assert.Equal($"https://{store.HostName}/c/chairs", address.Category("chairs"));
        Assert.Equal($"https://{store.HostName}/pages/delivery", address.ContentPage("delivery"));
        Assert.Equal($"https://{store.HostName}/api/storefront/store/logo", address.Logo);
        Assert.Equal($"https://{store.HostName}/api/storefront/products/1/images/2", address.Image("/api/storefront/products/1/images/2"));
    }

    // A slug is somebody's typing and ends up in a URL somebody else publishes.
    [Fact]
    public async Task A_slug_that_needs_escaping_is_escaped()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var address = await AddressAsync(store);

        Assert.Equal($"https://{store.HostName}/p/a%2Fb%20c", address!.Product("a/b c"));
    }

    // One of several domains is the one the shop calls home, and it is not the one that happened to ask.
    [Fact]
    public async Task A_store_with_several_domains_answers_with_the_primary_one()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var alias = await AddProvedDomainAsync(store, primary: false);

        var address = await AddressAsync(store);

        Assert.Equal(store.HostName, address!.Host);
        Assert.DoesNotContain(alias, address.Home, StringComparison.Ordinal);
    }

    // A shop nobody can reach has no absolute address, and saying so is better than building one that goes
    // nowhere — which is what the logo URL used to do.
    [Fact]
    public async Task A_store_with_no_proved_domain_has_no_address_at_all()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await UnproveEveryDomainAsync(store);

        var address = await AddressAsync(store);

        Assert.Null(address);
    }

    [Fact]
    public async Task The_language_is_the_stores_own_rather_than_its_whole_culture()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var address = await AddressAsync(store);

        Assert.Equal("en", address!.Language);
    }

    private async Task<StoreAddress?> AddressAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);

        return await scope.ServiceProvider.GetRequiredService<IStoreUrls>().FindAsync(CancellationToken);
    }

    private async Task<string> AddProvedDomainAsync(TestStore store, bool primary)
    {
        var hostName = TestStores.UniqueHostName("alias");
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var found = await dbContext.Set<Store>()
            .Include(candidate => candidate.Domains)
            .SingleAsync(candidate => candidate.Id == store.StoreId, CancellationToken);

        found.AddDomain(hostName, DateTimeOffset.UtcNow);

        if (primary)
        {
            found.MakePrimary(found.Domains.Single(domain => domain.HostName == hostName));
        }

        await dbContext.SaveChangesAsync(CancellationToken);

        return hostName;
    }

    private async Task UnproveEveryDomainAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE stores.store_domains SET verified_at = NULL WHERE store_id = {store.StoreId}",
            CancellationToken);
    }
}
