using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Shared.Maintenance;
using ShopForge.Stores.Domain;

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

    // A store starts with no delivery method (D-223), so going live also waits for one.
    [Fact]
    public async Task A_store_goes_live_only_once_it_has_branding_products_and_delivery()
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
        await AddDeliveryAsync(owner, store.Id);
        using var published = await owner.PostAsync($"/api/admin/stores/{store.Id}/publish", null, CancellationToken);
        using var storefront = await GetStorefrontAsync(hostName);

        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(
            [
                "The store has no active shipping method. A new store starts with none: add Packeta, or a delivery of the store's own.",
                "The store has no company details.",
                "The store has no logo.",
                "The store has no visible products.",
            ],
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

    // The defect V2 closes: store creation used to take whatever host name it was handed and mark it proved.
    // Host names are unique across the platform, so one company could take a name it did not own and the shop
    // that owned it could never add it — no DNS control needed, and no way for the victim to find out why
    // (D-189).
    [Theory]
    [InlineData("www.somebody-elses-shop.cz")]
    [InlineData("shop.microsoft.com")]
    [InlineData("example.org")]
    public async Task A_store_cannot_be_created_on_a_name_the_platform_does_not_own(string hostName)
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var response = await owner.PostAsJsonAsync("/api/admin/stores", NewStore("Squatter", hostName), CancellationToken);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(TestStores.PlatformSuffix, problem, StringComparison.Ordinal);

        // And the name is still free, which is the part that matters: a refused request must not leave a claim
        // behind that holds the name anyway.
        var taken = await factory.QueryAsync(existing, dbContext => dbContext.Set<StoreDomain>()
            .IgnoreQueryFilters()
            .AnyAsync(domain => domain.HostName == hostName, CancellationToken));
        Assert.False(taken);
    }

    // A bare label is the shape the API documents; the whole name under the suffix is what the admin shows back.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_is_created_on_a_label_the_platform_owns(bool wholeName)
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var label = TestStores.UniqueLabel("garden");
        var given = wholeName ? $"{label}.{TestStores.PlatformSuffix}" : label;

        using var response = await owner.PostAsJsonAsync("/api/admin/stores", NewStore("Garden Tools", given), CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<AdminStore>(CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{label}.{TestStores.PlatformSuffix}", created!.PrimaryHostName);
    }

    // A name inside the platform's own zone is not a merchant's to claim: they cannot put the proof in DNS for
    // it, so the claim would sit there holding the name until it lapsed (D-189).
    [Fact]
    public async Task A_merchant_cannot_claim_a_name_inside_the_platform_zone()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var response = await owner.PostAsJsonAsync(
            $"/api/admin/stores/{existing.StoreId}/domains",
            new { HostName = $"{TestStores.UniqueLabel("sneaky")}.{TestStores.PlatformSuffix}" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("belongs to the platform", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // The other half of the squatting problem: a claim nobody proves must give the name back, or the denial
    // simply moves from the domain table into the claim table (D-189).
    [Fact]
    public async Task An_unproved_claim_reports_when_it_lapses_and_the_sweep_releases_it()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var hostName = $"{TestStores.UniqueLabel("claimed")}.example.cz";

        using var claimed = await owner.PostAsJsonAsync(
            $"/api/admin/stores/{existing.StoreId}/domains", new { HostName = hostName }, CancellationToken);
        var domain = await claimed.Content.ReadFromJsonAsync<ClaimedDomain>(CancellationToken);

        Assert.Equal(HttpStatusCode.Created, claimed.StatusCode);
        Assert.NotNull(domain!.ClaimExpiresAt);
        Assert.NotNull(domain.ChallengeName);

        // Age the claim past its window, then run the housekeeping the worker runs.
        await factory.QueryAsync(existing, async dbContext =>
            await dbContext.Set<StoreDomain>()
                .Where(candidate => candidate.HostName == hostName)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(candidate => candidate.ClaimedAt, DateTimeOffset.UtcNow - StoreDomain.ClaimHoldsFor.Add(TimeSpan.FromMinutes(1))),
                    CancellationToken));

        var released = await SweepClaimsAsync(existing);

        Assert.True(released >= 1);

        var stillHeld = await factory.QueryAsync(existing, dbContext => dbContext.Set<StoreDomain>()
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.HostName == hostName, CancellationToken));
        Assert.False(stillHeld);
    }

    // The answer to "why can I not add my domain": a name belongs to one store platform-wide, and only an
    // operator can see who has it. It also reports names outside the platform's own zone, which is how a
    // domain given out before store creation was restricted would be found (D-189).
    [Fact]
    public async Task An_operator_can_see_who_holds_a_name()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));
        var claimed = $"{TestStores.UniqueLabel("held")}.example.cz";

        using var added = await owner.PostAsJsonAsync(
            $"/api/admin/stores/{existing.StoreId}/domains", new { HostName = claimed }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var platformName = await Held(operatorClient, existing.HostName);
        var merchantName = await Held(operatorClient, claimed);

        Assert.Equal(existing.StoreId, platformName.StoreId);
        Assert.Equal(existing.TenantId, platformName.TenantId);
        Assert.True(platformName.IsUnderPlatformSuffix);
        Assert.NotNull(platformName.VerifiedAt);
        Assert.Null(platformName.ClaimExpiresAt);

        Assert.Equal(existing.StoreId, merchantName.StoreId);
        Assert.False(merchantName.IsUnderPlatformSuffix);
        Assert.Null(merchantName.VerifiedAt);
        Assert.NotNull(merchantName.ClaimExpiresAt);
        Assert.False(merchantName.ClaimHasLapsed);
    }

    [Fact]
    public async Task A_tenant_cannot_see_who_holds_a_name()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, existing.TenantId));

        using var response = await owner.GetAsync("/api/platform/domains", CancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    // A proved name is never swept, however old the claim is.
    [Fact]
    public async Task A_proved_domain_is_not_released_by_the_sweep()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        await factory.QueryAsync(existing, async dbContext =>
            await dbContext.Set<StoreDomain>()
                .ExecuteUpdateAsync(
                    set => set.SetProperty(candidate => candidate.ClaimedAt, DateTimeOffset.UtcNow.AddYears(-1)),
                    CancellationToken));

        var released = await SweepClaimsAsync(existing);

        Assert.Equal(0, released);
        Assert.True(await factory.QueryAsync(existing, dbContext => dbContext.Set<StoreDomain>()
            .AnyAsync(candidate => candidate.HostName == existing.HostName, CancellationToken)));
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
        await AddDeliveryAsync(owner, store.Id);
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

    // Resolving it rather than constructing it also proves the module registered it: the worker finds its
    // housekeeping by this seam, so a stage that wrote the class and forgot the registration would pass every
    // other test here.
    private async Task<int> SweepClaimsAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);

        return await scope.ServiceProvider.GetServices<IStoreMaintenance>()
            .Single(housekeeping => housekeeping.Name == "stores.expired-domain-claims")
            .RunAsync(CancellationToken);
    }

    private async Task<PlatformDomain> Held(HttpClient operatorClient, string hostName)
    {
        var listed = await operatorClient.GetFromJsonAsync<PlatformDomainList>(
            $"/api/platform/domains?q={Uri.EscapeDataString(hostName)}", CancellationToken);

        return Assert.Single(listed!.Items);
    }

    private static CreateStore NewStore(string name, string hostName) =>
        new(name, hostName, "EUR", "en-IE", new Theme("#123456", "#FFFFFF", 6));

    // A delivery of the store's own, which a new store has to be given before it can go live (D-223).
    internal static async Task AddDeliveryAsync(HttpClient owner, Guid storeId)
    {
        using var response = await owner.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/shipping-methods",
            new { Name = "Own delivery", ProviderKey = "manual", Price = 99m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

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

    private sealed record PlatformDomainList(List<PlatformDomain> Items, int TotalCount);

    private sealed record PlatformDomain(
        string HostName,
        Guid StoreId,
        string StoreName,
        Guid TenantId,
        string TenantName,
        bool IsPrimary,
        DateTimeOffset? VerifiedAt,
        DateTimeOffset? ClaimExpiresAt,
        bool ClaimHasLapsed,
        bool IsUnderPlatformSuffix);

    private sealed record ClaimedDomain(Guid Id, string HostName, string? ChallengeName, DateTimeOffset? ClaimExpiresAt);

    private sealed record CreateStore(string Name, string HostName, string Currency, string Culture, Theme Theme);

    private sealed record Theme(string PrimaryColor, string SecondaryColor, int BorderRadius);

    private sealed record AdminStore(Guid Id, string Name, string Currency, string Culture, string Status, string? LogoUrl, string? PrimaryHostName);

    private sealed record PublishProblem(string Title, List<string> Problems);

    private sealed record StoreConfig(Guid Id, string Name);
}
