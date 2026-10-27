using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.IntegrationTests;

// Rules about the persistence model that no one feature's tests would notice being broken.
public sealed class ModelRulesTests(ShopForgeApiFactory factory)
{
    // Whatever a row owns is loaded with it by every query, so a row owning two collections is read as every item of one
    // joined to every item of the other, wherever it is loaded and for whatever reason. A second collection is kept
    // beside the row instead, as an order's returns, documents and parcels are (D-220).
    [Fact]
    public async Task No_row_owns_more_than_one_collection()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<DbContext>().Model;

        var owners = model.GetEntityTypes()
            .Where(type => !type.IsOwned())
            .Select(type => (type.ClrType.Name, Collections: OwnedCollections(type).ToList()))
            .Where(owner => owner.Collections.Count > 1)
            .Select(owner => $"{owner.Name}: {string.Join(", ", owner.Collections)}");

        Assert.Empty(owners);
    }

    private static IEnumerable<string> OwnedCollections(IEntityType type, string path = "")
    {
        foreach (var navigation in type.GetNavigations().Where(navigation => navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent))
        {
            if (navigation.IsCollection)
            {
                yield return path + navigation.Name;
            }

            foreach (var nested in OwnedCollections(navigation.TargetEntityType, $"{path}{navigation.Name}."))
            {
                yield return nested;
            }
        }
    }
}
