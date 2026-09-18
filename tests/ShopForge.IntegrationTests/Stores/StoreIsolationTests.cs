using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests.Stores;

public sealed class StoreIsolationTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task Store_A_cannot_read_store_B_data()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        await AssertSeesOnlyOwnDomainAsync(reader: storeA, other: storeB);
    }

    [Fact]
    public async Task Store_B_cannot_read_store_A_data()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        await AssertSeesOnlyOwnDomainAsync(reader: storeB, other: storeA);
    }

    [Fact]
    public async Task Store_owned_data_is_invisible_without_store_context()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, store: null);

        var visible = await Domains(scope)
            .Where(domain => domain.Id == storeA.DomainId || domain.Id == storeB.DomainId)
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, visible);
    }

    [Fact]
    public async Task Store_can_write_its_own_data()
    {
        var (storeA, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Add(new StoreDomain(storeA.StoreId, TestStores.UniqueHostName(), isPrimary: false));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, await Domains(scope).CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_cannot_insert_data_for_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var hostName = TestStores.UniqueHostName();
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Add(new StoreDomain(storeB.StoreId, hostName, isPrimary: false));

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.False(await Domains(scope).IgnoreQueryFilters().AnyAsync(domain => domain.HostName == hostName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_cannot_update_data_of_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var domainOfB = await Domains(scope).IgnoreQueryFilters().SingleAsync(domain => domain.Id == storeB.DomainId, TestContext.Current.CancellationToken);

        dbContext.Entry(domainOfB).Property(domain => domain.IsPrimary).CurrentValue = false;

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_cannot_move_its_data_to_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var domainOfA = await Domains(scope).SingleAsync(domain => domain.Id == storeA.DomainId, TestContext.Current.CancellationToken);

        dbContext.Entry(domainOfA).Property(domain => domain.StoreId).CurrentValue = storeB.StoreId;

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_cannot_delete_data_of_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var domainOfB = await Domains(scope).IgnoreQueryFilters().SingleAsync(domain => domain.Id == storeB.DomainId, TestContext.Current.CancellationToken);

        dbContext.Remove(domainOfB);

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_cannot_create_a_store_for_another_tenant()
    {
        var (storeA, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var otherTenant = new Tenant("Other tenant");

        dbContext.AddRange(otherTenant, new Store(otherTenant.Id, "Foreign store", "EUR", "en-IE", new StoreTheme("#000000", "#FFFFFF", 0)));

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task AssertSeesOnlyOwnDomainAsync(TestStore reader, TestStore other)
    {
        await using var scope = TestStores.CreateScope(factory.Services, reader);

        var visibleDomainIds = await Domains(scope)
            .Where(domain => domain.Id == reader.DomainId || domain.Id == other.DomainId)
            .Select(domain => domain.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var otherDomainById = await Domains(scope)
            .SingleOrDefaultAsync(domain => domain.Id == other.DomainId, TestContext.Current.CancellationToken);

        Assert.Equal([reader.DomainId], visibleDomainIds);
        Assert.Null(otherDomainById);
    }

    private static IQueryable<StoreDomain> Domains(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<DbContext>().Set<StoreDomain>();
}
