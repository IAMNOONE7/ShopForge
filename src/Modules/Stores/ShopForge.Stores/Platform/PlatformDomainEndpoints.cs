using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Admin;
using ShopForge.Shared.Http;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Platform;

// Who holds which host name, across every company on the platform. It exists because of the question a merchant
// asks when the admin refuses their domain: a name belongs to one store platform-wide, and the shop that cannot
// add it has no way to see who has it. Only an operator can (D-189).
//
// It also reports the names that predate the platform suffix: a store created before store creation was
// restricted may be proved on a name the platform does not own. Those are not corrected automatically — a live
// shop serving on one would stop — so they are listed for somebody to decide about.
internal static class PlatformDomainEndpoints
{
    public static void MapPlatformDomains(this IEndpointRouteBuilder platform)
    {
        platform.MapGet("/domains", GetDomainsAsync);
    }

    private static async Task<Ok<AdminListResponse<PlatformDomainResponse>>> GetDomainsAsync(
        DbContext dbContext,
        PlatformHosts platformHosts,
        TimeProvider clock,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null,
        string? q = null)
    {
        var listed = AdminListQuery.Of(page, pageSize, sort: null, q);
        var domains = dbContext.Set<StoreDomain>().IgnoreQueryFilters().AsNoTracking();

        if (listed.Pattern is { } pattern)
        {
            domains = domains.Where(domain => EF.Functions.Like(domain.HostName.ToLower(), pattern));
        }

        var total = await domains.CountAsync(cancellationToken);
        var rows = await (
                from domain in domains
                join store in dbContext.Set<Store>().IgnoreQueryFilters() on domain.StoreId equals store.Id
                join tenant in dbContext.Set<Tenant>().IgnoreQueryFilters() on store.TenantId equals tenant.Id
                orderby domain.HostName
                select new
                {
                    domain.HostName,
                    domain.IsPrimary,
                    domain.VerifiedAt,
                    domain.ClaimedAt,
                    StoreId = store.Id,
                    StoreName = store.Name,
                    TenantId = tenant.Id,
                    TenantName = tenant.Name,
                })
            .Skip(listed.Skipped)
            .Take(listed.Taken)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var items = rows
            .Select(row => new PlatformDomainResponse(
                row.HostName,
                row.StoreId,
                row.StoreName,
                row.TenantId,
                row.TenantName,
                row.IsPrimary,
                row.VerifiedAt,
                row.VerifiedAt is null ? row.ClaimedAt + StoreDomain.ClaimHoldsFor : null,
                row.VerifiedAt is null && row.ClaimedAt + StoreDomain.ClaimHoldsFor <= now,
                platformHosts.Owns(row.HostName)))
            .ToList();

        return TypedResults.Ok(new AdminListResponse<PlatformDomainResponse>(items, total, listed.Page, listed.PageSize));
    }
}

internal sealed record PlatformDomainResponse(
    string HostName,
    Guid StoreId,
    string StoreName,
    Guid TenantId,
    string TenantName,
    bool IsPrimary,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ClaimExpiresAt,
    // True when the sweep has not come round yet. An operator looking at a name somebody is waiting for wants to
    // know it is already gone in all but fact.
    bool ClaimHasLapsed,
    // False for a proved name outside the platform's own zone, which is either a merchant's own domain — normal
    // — or a name given out before store creation was restricted. The two are told apart by whether it is the
    // store's only name.
    bool IsUnderPlatformSuffix);
