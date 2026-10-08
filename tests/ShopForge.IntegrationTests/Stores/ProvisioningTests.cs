using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShopForge.Access.Domain;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Stores;

public sealed class ProvisioningTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_store_is_a_draft_and_does_not_serve_its_host()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var hostName = TestStores.UniqueHostName("new-store");

        var store = await CreateStoreAsync(owner, "Garden Tools", hostName);
        var stores = await owner.GetFromJsonAsync<List<AdminStore>>("/api/admin/stores", CancellationToken);
        using var storefront = await GetStorefrontAsync(hostName);

        Assert.Equal("draft", store.Status);
        Assert.Contains(stores!, candidate => candidate.Id == store.Id && candidate.PrimaryHostName == hostName);
        Assert.Equal(HttpStatusCode.NotFound, storefront.StatusCode);
    }

    [Fact]
    public async Task A_store_goes_live_only_once_it_has_branding_and_products()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var hostName = TestStores.UniqueHostName("launch");
        var store = await CreateStoreAsync(owner, "Garden Tools", hostName);

        using var tooEarly = await owner.PostAsync($"/api/admin/stores/{store.Id}/publish", null, CancellationToken);
        var problems = (await tooEarly.Content.ReadFromJsonAsync<PublishProblem>(CancellationToken))!.Problems;

        await UploadLogoAsync(owner, store.Id);
        await owner.ListProductAsync(store.Id, await owner.CreateProductAsync(), "Spade", 19.90m);
        await SetCompanyAsync(owner, store.Id);
        using var published = await owner.PostAsync($"/api/admin/stores/{store.Id}/publish", null, CancellationToken);
        using var storefront = await GetStorefrontAsync(hostName);

        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(
            ["The store has no company details.", "The store has no logo.", "The store has no visible products."],
            problems.Order());
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(HttpStatusCode.OK, storefront.StatusCode);
        Assert.Equal("Garden Tools", (await storefront.Content.ReadFromJsonAsync<StoreConfig>(CancellationToken))!.Name);
    }

    [Fact]
    public async Task Unpublishing_takes_the_store_off_its_host_at_once()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var live = await GetStorefrontAsync(existing.HostName);
        using var unpublished = await owner.PostAsync($"/api/admin/stores/{existing.StoreId}/unpublish", null, CancellationToken);
        using var offline = await GetStorefrontAsync(existing.HostName);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, offline.StatusCode);
    }

    [Fact]
    public async Task Host_names_belong_to_one_store_only()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var response = await owner.PostAsJsonAsync("/api/admin/stores", NewStore("Copycat", existing.HostName), CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("currency", "EURO")]
    [InlineData("culture", "xx-NOPE")]
    [InlineData("hostName", "not a host")]
    [InlineData("name", "")]
    public async Task Invalid_store_settings_are_rejected(string field, string value)
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var request = NewStore("Garden Tools", TestStores.UniqueHostName());
        var payload = new Dictionary<string, object?>
        {
            ["name"] = field == "name" ? value : request.Name,
            ["hostName"] = field == "hostName" ? value : request.HostName,
            ["currency"] = field == "currency" ? value : request.Currency,
            ["culture"] = field == "culture" ? value : request.Culture,
            ["theme"] = request.Theme,
        };

        using var response = await owner.PostAsJsonAsync("/api/admin/stores", payload, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    // A well-formed code ShopForge has no minor units for is not a currency it can charge in, and a merchant has
    // to be told so rather than shown a 500 (D-186).
    [Fact]
    public async Task A_currency_ShopForge_cannot_charge_in_is_refused_with_a_reason()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var created = await owner.PostAsJsonAsync(
            "/api/admin/stores",
            NewStore("Harare Shop", TestStores.UniqueHostName()) with { Currency = "ZWL" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("ShopForge can charge in", await created.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Currency_can_only_change_while_the_store_is_a_draft()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var store = await CreateStoreAsync(owner, "Garden Tools", TestStores.UniqueHostName());

        using var draftChange = await owner.PutAsJsonAsync(
            $"/api/admin/stores/{store.Id}",
            new { Name = "Garden Tools", Currency = "CZK", Culture = "cs-CZ", Theme = NewStore("x", "y").Theme },
            CancellationToken);
        await UploadLogoAsync(owner, store.Id);
        await owner.ListProductAsync(store.Id, await owner.CreateProductAsync(), "Spade", 19.90m);
        await SetCompanyAsync(owner, store.Id);
        using var publish = await owner.PostAsync($"/api/admin/stores/{store.Id}/publish", null, CancellationToken);
        using var publishedChange = await owner.PutAsJsonAsync(
            $"/api/admin/stores/{store.Id}",
            new { Name = "Garden Tools", Currency = "EUR", Culture = "cs-CZ", Theme = NewStore("x", "y").Theme },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, draftChange.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, publishedChange.StatusCode);
    }

    [Fact]
    public async Task Only_owners_and_admins_manage_the_store_lifecycle()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var catalogManager = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId, TenantRole.CatalogManager));

        using var create = await catalogManager.PostAsJsonAsync("/api/admin/stores", NewStore("Garden Tools", TestStores.UniqueHostName()), CancellationToken);
        using var publish = await catalogManager.PostAsync($"/api/admin/stores/{existing.StoreId}/publish", null, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }

    [Fact]
    public async Task A_tenant_cannot_change_another_tenants_store()
    {
        var (foreignStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (ownStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, ownStore.TenantId));

        using var publish = await owner.PostAsync($"/api/admin/stores/{foreignStore.StoreId}/publish", null, CancellationToken);
        using var settings = await owner.PutAsJsonAsync(
            $"/api/admin/stores/{foreignStore.StoreId}",
            new { Name = "Taken over", Currency = "EUR", Culture = "en-IE", Theme = NewStore("x", "y").Theme },
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settings.StatusCode);
    }

    private static CreateStore NewStore(string name, string hostName) =>
        new(name, hostName, "EUR", "en-IE", new Theme("#123456", "#FFFFFF", 6));

    internal static async Task SetCompanyAsync(HttpClient owner, Guid storeId, string name = "Garden Tools")
    {
        using var response = await owner.PutAsJsonAsync(
            $"/api/admin/stores/{storeId}",
            new
            {
                Name = name,
                Currency = "CZK",
                Culture = "cs-CZ",
                Theme = new { PrimaryColor = "#123456", SecondaryColor = "#654321", BorderRadius = 4 },
                Company = new
                {
                    LegalName = $"{name} s.r.o.",
                    Line1 = "1 Trade Street",
                    City = "Brno",
                    PostalCode = "602 00",
                    Country = "CZ",
                    RegistrationNumber = "12345678",
                    VatNumber = "CZ12345678",
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<AdminStore> CreateStoreAsync(HttpClient owner, string name, string hostName)
    {
        using var response = await owner.PostAsJsonAsync("/api/admin/stores", NewStore(name, hostName), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminStore>(CancellationToken))!;
    }

    private static async Task UploadLogoAsync(HttpClient owner, Guid storeId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(AdminCatalogApi.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");

        using var response = await owner.PutAsync($"/api/admin/stores/{storeId}/logo", form, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetStorefrontAsync(string hostName)
    {
        using var client = factory.CreateClient();

        return await client.GetAsync($"http://{hostName}/api/storefront/store", CancellationToken);
    }

    private sealed record CreateStore(string Name, string HostName, string Currency, string Culture, Theme Theme);

    private sealed record Theme(string PrimaryColor, string SecondaryColor, int BorderRadius);

    private sealed record AdminStore(Guid Id, string Name, string Currency, string Culture, string Status, string? LogoUrl, string? PrimaryHostName);

    private sealed record PublishProblem(string Title, List<string> Problems);

    private sealed record StoreConfig(Guid Id, string Name);
}
