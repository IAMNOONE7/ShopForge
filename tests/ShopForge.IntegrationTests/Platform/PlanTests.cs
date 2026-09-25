using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Platform;

public sealed class PlanTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Nobody's limits change the day plans arrive: a company nobody has put on a plan is on the uncapped one.
    [Fact]
    public async Task A_tenant_nobody_has_moved_is_on_the_default_plan_and_capped_by_nothing()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        var plan = await owner.GetFromJsonAsync<AdminPlanView>("/api/admin/plan", CancellationToken);
        using var created = await owner.PostAsJsonAsync("/api/admin/stores", NewStore(), CancellationToken);

        Assert.Equal(("scale", (int?)null, (int?)null), (plan!.Code, plan.MaxStores, plan.MaxProducts));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task A_plan_caps_the_stores_a_company_may_add()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var operatorClient = await SignInAsync();
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        await MoveToAsync(operatorClient, store.TenantId, "starter");

        // The company already has two stores, which a cap of one leaves alone; it only stops the next.
        using var refused = await owner.PostAsJsonAsync("/api/admin/stores", NewStore(), CancellationToken);
        var problem = await refused.Content.ReadAsStringAsync(CancellationToken);
        var plan = await owner.GetFromJsonAsync<AdminPlanView>("/api/admin/plan", CancellationToken);

        await MoveToAsync(operatorClient, store.TenantId, "growth");
        using var allowed = await owner.PostAsJsonAsync("/api/admin/stores", NewStore(), CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("no more stores", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("starter", 1, 100), (plan!.Code, plan.MaxStores, plan.MaxProducts));
        Assert.Equal(2, plan.Usage.Single(count => count.Name == "stores").Value);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task A_plan_caps_the_products_a_company_may_add()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var operatorClient = await SignInAsync();
        await MoveToAsync(operatorClient, furniture.Store.TenantId, "tiny", maxProducts: 4);

        using var refused = await furniture.Admin.PostAsJsonAsync("/api/admin/products", new { Sku = $"SKU-{Guid.NewGuid():N}" }, CancellationToken);
        var problem = await refused.Content.ReadAsStringAsync(CancellationToken);

        await MoveToAsync(operatorClient, furniture.Store.TenantId, "scale");
        using var allowed = await furniture.Admin.PostAsJsonAsync("/api/admin/products", new { Sku = $"SKU-{Guid.NewGuid():N}" }, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("no more products", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    // Half a catalog is worse than a clear refusal, so a file that would cross the cap imports nothing.
    [Fact]
    public async Task An_import_that_would_cross_the_cap_imports_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var operatorClient = await SignInAsync();
        await MoveToAsync(operatorClient, furniture.Store.TenantId, "small", maxProducts: 5);

        var report = await ImportAsync(furniture, ["IMPORT-A", "IMPORT-B"]);
        var afterRefusal = await ProductCountAsync(furniture);
        await MoveToAsync(operatorClient, furniture.Store.TenantId, "scale");
        var second = await ImportAsync(furniture, ["IMPORT-A", "IMPORT-B"]);

        Assert.Equal(0, report.Created);
        Assert.Contains(report.Issues, issue => issue.Message.Contains("plan does not cover", StringComparison.Ordinal));
        Assert.Equal(4, afterRefusal);
        Assert.Equal(2, second.Created);
        Assert.Equal(6, await ProductCountAsync(furniture));
    }

    [Fact]
    public async Task The_platform_sees_which_plan_a_tenant_is_on()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var operatorClient = await SignInAsync();

        await MoveToAsync(operatorClient, store.TenantId, "growth");
        var tenants = await operatorClient.GetFromJsonAsync<List<PlatformTenantView>>("/api/platform/tenants", CancellationToken);
        var plans = await operatorClient.GetFromJsonAsync<List<PlanView>>("/api/platform/plans", CancellationToken);

        Assert.Equal("growth", tenants!.Single(tenant => tenant.Id == store.TenantId).PlanCode);
        Assert.Contains(plans!, plan => plan is { Code: "scale", IsDefault: true, MaxStores: null });
    }

    [Fact]
    public async Task Only_the_platform_changes_plans()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        using var operatorClient = await SignInAsync();
        var growth = await PlanIdAsync(operatorClient, "growth");

        using var byTheTenant = await owner.PutAsJsonAsync($"/api/platform/tenants/{store.TenantId}/plan", new { PlanId = growth }, CancellationToken);
        using var listedByTheTenant = await owner.GetAsync("/api/platform/plans", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, byTheTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listedByTheTenant.StatusCode);
    }

    private static object NewStore() => new
    {
        Name = "Another shop",
        HostName = TestStores.UniqueHostName(),
        Currency = "EUR",
        Culture = "en-IE",
        Theme = new { PrimaryColor = "#112233", SecondaryColor = "#FFFFFF", BorderRadius = 4 },
    };

    private async Task<Guid> PlanIdAsync(HttpClient operatorClient, string code)
    {
        var plans = await operatorClient.GetFromJsonAsync<List<PlanView>>("/api/platform/plans", CancellationToken);

        return plans!.Single(plan => plan.Code == code).Id;
    }

    // Moves the tenant to a plan, making it first when the test wants caps of its own.
    private async Task MoveToAsync(HttpClient operatorClient, Guid tenantId, string code, int? maxStores = null, int? maxProducts = null)
    {
        var plans = await operatorClient.GetFromJsonAsync<List<PlanView>>("/api/platform/plans", CancellationToken);
        var plan = plans!.SingleOrDefault(candidate => candidate.Code == code);

        if (plan is null)
        {
            using var created = await operatorClient.PostAsJsonAsync(
                "/api/platform/plans",
                new { Code = code, Name = code, MaxStores = maxStores, MaxProducts = maxProducts },
                CancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            plan = (await created.Content.ReadFromJsonAsync<PlanView>(CancellationToken))!;
        }

        using var moved = await operatorClient.PutAsJsonAsync(
            $"/api/platform/tenants/{tenantId}/plan",
            new { PlanId = plan.Id },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    private async Task<ImportReportView> ImportAsync(FurnitureStore furniture, string[] skus)
    {
        object?[][] rows = [.. skus.Select(sku => new object?[] { sku, sku, 10m, 21m })];
        using var response = await furniture.Admin.ImportAsync(
            furniture.Store.StoreId,
            ImportFiles.Workbook(["sku", "name", "price", "vat"], rows));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ImportReportView>(CancellationToken))!;
    }

    private Task<int> ProductCountAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM catalog.products WHERE tenant_id = {furniture.Store.TenantId}")
            .SingleAsync(CancellationToken));

    private Task<HttpClient> SignInAsync() => TestPlatformUsers.SignInAsync(factory, CancellationToken);

    private sealed record AdminPlanView(string? Code, string? Name, int? MaxStores, int? MaxProducts, List<UsageView> Usage);

    private sealed record PlanView(Guid Id, string Code, string Name, int? MaxStores, int? MaxProducts, bool IsDefault);

    private sealed record PlatformTenantView(Guid Id, string Name, string Status, string? PlanCode);

    private sealed record UsageView(string Name, int Value);

    private sealed record ImportReportView(int Created, int Updated, int Unchanged, int Invalid, int Failed, List<ImportIssueView> Issues);

    private sealed record ImportIssueView(int Row, string? Column, string Message);
}
