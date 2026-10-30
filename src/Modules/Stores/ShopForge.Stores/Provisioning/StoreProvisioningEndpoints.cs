using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Platform;
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
        storeAdmin.MapGet("/readiness", GetReadinessAsync);
        storeAdmin.MapPost("/unpublish", UnpublishAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static async Task<Results<Created<AdminStoreResponse>, ValidationProblem, ProblemHttpResult>> CreateStoreAsync(
        CreateStoreRequest request,
        DbContext dbContext,
        StoreContext storeContext,
        ITenantLimits limits,
        IEnumerable<IStoreInitializer> initializers,
        PlatformHosts platformHosts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // The name a new store is given is the platform's to give: a label under the suffix the platform owns,
        // and nothing else. A merchant's own domain is added afterwards and proved (D-189).
        var label = platformHosts.LabelOf(request.HostName);
        var errors = ValidateSettings(request.Name, request.Currency, request.Culture, request.Theme)
            .Check(platformHosts.IsConfigured, "hostName", "The platform has no host name suffix configured, so it cannot give a store an address.")
            .Check(!platformHosts.IsConfigured || label is not null, "hostName", $"A name of lower-case letters, digits and hyphens under {platformHosts.Suffix} is required, for example wooden-home. A domain of your own is added and proved after the store exists.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        // What the company's plan allows, counted against what it has. A cap never removes a store, so a tenant
        // moved to a smaller plan keeps everything and simply cannot add more (D-108).
        if (await limits.MaxAsync(TenantResource.Stores, cancellationToken) is { } maxStores)
        {
            var stores = await dbContext.Set<Store>()
                .IgnoreQueryFilters([TenancyFilters.Store])
                .CountAsync(cancellationToken);

            if (stores >= maxStores)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This plan allows no more stores",
                    detail: $"The plan covers {maxStores} {(maxStores == 1 ? "store" : "stores")} and the company has {stores}.");
            }
        }

        var hostName = platformHosts.Compose(label!);

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
        store.AddPlatformDomain(platformHosts, label!, clock.GetUtcNow());

        // Adding the store's own domain is a write inside the new store, so the tenant scope is narrowed to it first.
        storeContext.Set(store.Id, store.TenantId);
        dbContext.Add(store);

        // Modules set up what the new store needs to work, for example its payment and shipping methods.
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(new NewStore(store.Currency), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{store.Id}", AdminStoreResponse.From(store, hostName));
    }

    private static async Task<Results<Ok<AdminStoreResponse>, ValidationProblem, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateStoreRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IAuditLog audit,
        IOutbox outbox,
        CancellationToken cancellationToken)
    {
        var company = request.Company?.ToCompany();
        var errors = ValidateSettings(request.Name, request.Currency, request.Culture, request.Theme)
            .Check(request.ReturnWindowDays is null or (>= 0 and <= 365), "returnWindowDays", "A return window is between zero and 365 days.")
            .Check(request.Company is null || company is not null, "company", "Legal name, address and registration number are required.")
            .Check(request.Seo?.TitleSuffix is null || request.Seo.TitleSuffix.Trim().Length <= 200, "seo.titleSuffix", "A title suffix can be up to 200 characters.")
            .Check(request.Seo?.Description is null || request.Seo.Description.Trim().Length <= 500, "seo.description", "A description can be up to 500 characters.")
            .Check(IsAnAddressOrNothing(request.Seo?.SocialImageUrl), "seo.socialImageUrl", "A social image must be an absolute http or https address.")
            .Check(request.FreeShipping is not { IsOn: true, Threshold: null }, "freeShipping.threshold", "Say what the goods must come to for delivery to be free.")
            .Check(request.FreeShipping?.Threshold is null or > 0, "freeShipping.threshold", "A threshold is more than nothing.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);

        // In the currency the store will have once this is saved, which the request may be changing.
        var currency = request.Currency is { } code ? Currency.Of(code) : store.Currency;
        var threshold = request.FreeShipping is { } sent ? sent.Threshold : store.FreeShippingThreshold;

        if (threshold is { } amount && !currency.Holds(amount))
        {
            return new RequestErrors()
                .Check(false, "freeShipping.threshold", $"A threshold has at most {currency.Decimals} decimals in {currency.Code}.")
                .ToProblem();
        }

        var freeShippingBefore = new { IsOn = store.FreeShippingIsOn, Threshold = store.FreeShippingThreshold };
        var cultureBefore = store.Culture;

        try
        {
            store.UpdateSettings(
                request.Name!,
                request.Currency,
                request.Culture!,
                request.Theme!.ToTheme(),
                request.ReturnWindowDays ?? store.ReturnWindowDays);

            if (company is not null)
            {
                store.SetCompany(company);
            }

            // Absent leaves the store's answers alone; present replaces all four, so clearing one is sending
            // it empty rather than leaving it out.
            if (request.Seo is { } seo)
            {
                store.DescribeToSearchEngines(seo.TitleSuffix, seo.Description, seo.SocialImageUrl, seo.NoIndex);
            }

            // Absent leaves free delivery as it is; present replaces both, so a threshold survives being switched off
            // only when it is sent (D-221).
            if (request.FreeShipping is { } freeShipping)
            {
                store.SetFreeShipping(freeShipping.IsOn, freeShipping.Threshold);
            }
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);
        }

        var freeShippingAfter = new { IsOn = store.FreeShippingIsOn, Threshold = store.FreeShippingThreshold };

        // A commercial term every quote and order follows, so who changed it, and from what, is kept.
        if (!freeShippingAfter.Equals(freeShippingBefore))
        {
            audit.Record("store.free-shipping-changed", store.Name, new { Before = freeShippingBefore, After = freeShippingAfter });
        }

        // Search reads a store's words by its language, so they are read again in the new one (D-224).
        if (store.Culture != cultureBefore)
        {
            outbox.Enqueue(new StoreCultureChanged(store.Culture));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminStoreResponse.From(store, PrimaryHostName(store)));
    }

    // The same answer the merchant can read before they press anything, so what refuses them is what they were
    // already looking at. A published store answers too: a provider disconnected after publication is a problem
    // the overview should carry rather than one a shopper discovers at the checkout (D-190).
    private static async Task<Ok<StoreReadinessResponse>> GetReadinessAsync(
        IEnumerable<IStorePublishCheck> checks,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ReadinessAsync(checks, cancellationToken));

    private static async Task<Results<Ok<AdminStoreResponse>, ProblemHttpResult>> PublishAsync(
        IEnumerable<IStorePublishCheck> checks,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var readiness = await ReadinessAsync(checks, cancellationToken);

        if (!readiness.IsReady)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The store is not ready to be published",
                extensions: new Dictionary<string, object?>
                {
                    // The prose, as before, so a client that only reads this keeps working; and the whole
                    // checklist beside it, so one that wants to say which line failed can.
                    ["problems"] = readiness.Checks.Where(check => check.Blocks).Select(check => check.Problem).ToList(),
                    ["readiness"] = readiness.Checks,
                });
        }

        return TypedResults.Ok(await ChangeStatusAsync(dbContext, storeContext, resolver, audit, publish: true, cancellationToken));
    }

    private static async Task<StoreReadinessResponse> ReadinessAsync(
        IEnumerable<IStorePublishCheck> checks,
        CancellationToken cancellationToken)
    {
        var answers = new List<StoreReadiness>();

        foreach (var check in checks)
        {
            answers.AddRange(await check.CheckAsync(cancellationToken));
        }

        return new StoreReadinessResponse(answers.All(answer => !answer.Blocks), [.. answers.OrderBy(answer => answer.Check, StringComparer.Ordinal)]);
    }

    private static async Task<Ok<AdminStoreResponse>> UnpublishAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IAuditLog audit,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ChangeStatusAsync(dbContext, storeContext, resolver, audit, publish: false, cancellationToken));

    private static async Task<AdminStoreResponse> ChangeStatusAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IAuditLog audit,
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

        audit.Record(publish ? "store.published" : "store.unpublished", store.Name);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Host lookups are cached, so a store going live (or offline) has to drop its cached entries.
        await resolver.ForgetAsync(store.Domains.Select(domain => domain.HostName), cancellationToken);

        return AdminStoreResponse.From(store, PrimaryHostName(store));
    }

    private static Task<Store> CurrentStoreAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .Include(store => store.Domains)
            .IgnoreQueryFilters([TenancyFilters.Store])
            .SingleAsync(store => store.Id == storeContext.StoreId, cancellationToken);

    private static string? PrimaryHostName(Store store) =>
        store.Domains.FirstOrDefault(domain => domain.IsPrimary)?.HostName;

    // A social image is published to whoever shares a link, so a relative path is no use to them; it is an
    // address or it is nothing.
    private static bool IsAnAddressOrNothing(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            && url.Trim().Length <= 2000);

    private static RequestErrors ValidateSettings(string? name, string? currency, string? culture, ThemeRequest? theme) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(currency is null || IsCurrencyCode(currency), "currency", "Currency must be a currency ShopForge can charge in, for example EUR.")
            .Check(IsCulture(culture), "culture", "Culture must be a known culture name, for example en-IE.")
            .Check(theme is not null && theme.IsValid, "theme", "Theme needs two #RRGGBB colors and a border radius of zero or more.");

    // Not merely three letters: a code ShopForge has no minor units for cannot be charged, and a merchant hears
    // that here rather than through a 500 from the domain refusing it (D-186).
    private static bool IsCurrencyCode(string currency) => Currency.Find(currency) is not null;

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

// Ready means nothing required is failing. Advice can be outstanding on a shop that is perfectly publishable.
internal sealed record StoreReadinessResponse(bool IsReady, List<StoreReadiness> Checks);

internal sealed record CreateStoreRequest(string? Name, string? HostName, string? Currency, string? Culture, ThemeRequest? Theme);

internal sealed record UpdateStoreRequest(
    string? Name,
    string? Currency,
    string? Culture,
    ThemeRequest? Theme,
    int? ReturnWindowDays,
    CompanyRequest? Company,
    SeoRequest? Seo,
    FreeShippingRequest? FreeShipping = null);

internal sealed record FreeShippingRequest(bool IsOn, decimal? Threshold);

internal sealed record SeoRequest(string? TitleSuffix, string? Description, string? SocialImageUrl, bool NoIndex);

internal sealed record CompanyRequest(
    string? LegalName,
    string? Line1,
    string? City,
    string? PostalCode,
    string? Country,
    string? RegistrationNumber,
    string? VatNumber)
{
    public StoreCompany? ToCompany() =>
        StoreCompany.From(LegalName, Line1, City, PostalCode, Country, RegistrationNumber, VatNumber);
}

internal sealed record ThemeRequest(string? PrimaryColor, string? SecondaryColor, int BorderRadius)
{
    public bool IsValid => PrimaryColor is not null && SecondaryColor is not null && BorderRadius >= 0 && Colors().All(IsHexColor);

    public StoreTheme ToTheme() => new(PrimaryColor!, SecondaryColor!, BorderRadius);

    private IEnumerable<string> Colors() => [PrimaryColor!, SecondaryColor!];

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(char.IsAsciiHexDigit);
}
