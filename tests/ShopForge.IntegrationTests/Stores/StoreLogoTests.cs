using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Shared.Files;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests.Stores;

public sealed class StoreLogoTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uploaded_logo_is_shown_by_its_store_only()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, storeA.TenantId));
        using var storefront = factory.CreateClient();

        using var upload = await UploadLogoAsync(owner, storeA);
        var configA = await storefront.GetFromJsonAsync<StoreConfig>($"http://{storeA.HostName}/api/storefront/store", CancellationToken);
        var configB = await storefront.GetFromJsonAsync<StoreConfig>($"http://{storeB.HostName}/api/storefront/store", CancellationToken);
        using var logoA = await storefront.GetAsync($"http://{storeA.HostName}{configA!.LogoUrl}", CancellationToken);
        using var logoB = await storefront.GetAsync($"http://{storeB.HostName}/api/storefront/store/logo", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal("image/png", logoA.Content.Headers.ContentType?.MediaType);
        Assert.Null(configB!.LogoUrl);
        Assert.Equal(HttpStatusCode.NotFound, logoB.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_logo_removes_the_previous_file()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        using var first = await UploadLogoAsync(owner, store);
        var firstPath = await LogoPathAsync(store);
        using var second = await UploadLogoAsync(owner, store);
        var secondPath = await LogoPathAsync(store);

        var fileStorage = factory.Services.GetRequiredService<IFileStorage>();
        Assert.NotEqual(firstPath, secondPath);
        Assert.Null(await fileStorage.OpenReadAsync(firstPath!, CancellationToken));
        Assert.NotNull(await fileStorage.OpenReadAsync(secondPath!, CancellationToken));
    }

    [Fact]
    public async Task Only_owners_and_admins_can_change_store_branding()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var catalogManager = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.CatalogManager));

        using var response = await UploadLogoAsync(catalogManager, store);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadLogoAsync(HttpClient admin, TestStore store)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(AdminCatalogApi.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");

        return await admin.PutAsync($"/api/admin/stores/{store.StoreId}/logo", form, CancellationToken);
    }

    private async Task<string?> LogoPathAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Store>()
            .Where(candidate => candidate.Id == store.StoreId)
            .Select(candidate => candidate.LogoPath)
            .SingleAsync(CancellationToken);
    }

    private sealed record StoreConfig(Guid Id, string Name, string? LogoUrl);
}
