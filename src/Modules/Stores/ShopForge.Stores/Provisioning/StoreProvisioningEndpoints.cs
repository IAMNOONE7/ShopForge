using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Admin;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Provisioning;

internal static class StoreProvisioningEndpoints
{
    public static void MapStoreProvisioning(this IEndpointRouteBuilder tenantAdmin) =>
        tenantAdmin.MapPost("/stores", CreateStoreAsync).RequireAuthorization(AdminPolicies.StoreManagement);

    public static void MapStoreLifecycle(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapPut("/", UpdateSettingsAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/publish", PublishAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/unpublish", UnpublishAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static async Task<Results<Created<AdminStoreResponse>, ValidationProblem, ProblemHttpResult>> CreateStoreAsync(
        CreateStoreRequest request,
        DbContext dbContext,
        StoreContext storeContext,
        IEnumerable<IStoreInitializer> initializers,
        CancellationToken cancellationToken)
    {
        var hostName = HostNames.Normalize(request.HostName);
        var errors = ValidateSettings(request.Name, request.Currency, request.Culture, request.Theme)
            .Check(hostName is not null, "hostName", "A valid host name is required, for example shop.example.com.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<StoreDomain>().IgnoreQueryFilters().AnyAsync(domain => domain.HostName == hostName, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The host name is already used by another store");
        }

        var store = new Store(
            storeContext.TenantId!.Value,
            request.Name!,
            request.Currency!,
            request.Culture!,
            request.Theme!.ToTheme());
        store.AddDomain(hostName!);

        // Adding the store's own domain is a write inside the new store, so the tenant scope is narrowed to it first.
        storeContext.Set(store.Id, store.TenantId);
        dbContext.Add(store);

        // Modules set up what the new store needs to work, for example its payment and shipping methods.
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{store.Id}", AdminStoreResponse.From(store, hostName));
    }

    private static async Task<Results<Ok<AdminStoreResponse>, ValidationProblem, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateStoreRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateSettings(request.Name, request.Currency, request.Culture, request.Theme);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);

        try
        {
            store.UpdateSettings(request.Name!, request.Currency, request.Culture!, request.Theme!.ToTheme());
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminStoreResponse.From(store, PrimaryHostName(store)));
    }

    private static async Task<Results<Ok<AdminStoreResponse>, ProblemHttpResult>> PublishAsync(
        IEnumerable<IStorePublishCheck> checks,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        foreach (var check in checks)
        {
            if (await check.FindProblemAsync(cancellationToken) is { } problem)
            {
                problems.Add(problem);
            }
        }

        if (problems.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The store is not ready to be published",
                extensions: new Dictionary<string, object?> { ["problems"] = problems });
        }

        return TypedResults.Ok(await ChangeStatusAsync(dbContext, storeContext, resolver, publish: true, cancellationToken));
    }

    private static async Task<Ok<AdminStoreResponse>> UnpublishAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ChangeStatusAsync(dbContext, storeContext, resolver, publish: false, cancellationToken));

    private static async Task<AdminStoreResponse> ChangeStatusAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        bool publish,
        CancellationToken cancellationToken)
    {
        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);

        if (publish)
        {
            store.Publish();
        }
        else
        {
            store.Unpublish();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Host lookups are cached, so a store going live (or offline) has to drop its cached entries.
        resolver.Forget(store.Domains.Select(domain => domain.HostName));

        return AdminStoreResponse.From(store, PrimaryHostName(store));
    }

    private static Task<Store> CurrentStoreAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .Include(store => store.Domains)
            .IgnoreQueryFilters([TenancyFilters.Store])
            .SingleAsync(store => store.Id == storeContext.StoreId, cancellationToken);

    private static string? PrimaryHostName(Store store) =>
        store.Domains.FirstOrDefault(domain => domain.IsPrimary)?.HostName;

    private static RequestErrors ValidateSettings(string? name, string? currency, string? culture, ThemeRequest? theme) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(currency is null || IsCurrencyCode(currency), "currency", "Currency must be a three-letter ISO 4217 code, for example EUR.")
            .Check(IsCulture(culture), "culture", "Culture must be a known culture name, for example en-IE.")
            .Check(theme is not null && theme.IsValid, "theme", "Theme needs two #RRGGBB colors and a border radius of zero or more.");

    private static bool IsCurrencyCode(string currency) =>
        currency.Trim().Length == 3 && currency.Trim().All(char.IsAsciiLetter);

    private static bool IsCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return false;
        }

        try
        {
            return CultureInfo.GetCultureInfo(culture, predefinedOnly: true) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

internal sealed record CreateStoreRequest(string? Name, string? HostName, string? Currency, string? Culture, ThemeRequest? Theme);

internal sealed record UpdateStoreRequest(string? Name, string? Currency, string? Culture, ThemeRequest? Theme);

internal sealed record ThemeRequest(string? PrimaryColor, string? SecondaryColor, int BorderRadius)
{
    public bool IsValid => PrimaryColor is not null && SecondaryColor is not null && BorderRadius >= 0 && Colors().All(IsHexColor);

    public StoreTheme ToTheme() => new(PrimaryColor!, SecondaryColor!, BorderRadius);

    private IEnumerable<string> Colors() => [PrimaryColor!, SecondaryColor!];

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(char.IsAsciiHexDigit);
}
