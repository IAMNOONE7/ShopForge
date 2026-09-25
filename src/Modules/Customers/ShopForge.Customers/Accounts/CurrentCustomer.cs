using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Accounts;

internal sealed class CurrentCustomer(IHttpContextAccessor httpContextAccessor, IStoreContext storeContext) : ICurrentCustomer
{
    // A session only counts for the store it was created in, whatever host the cookie arrived on.
    public async Task<CurrentCustomerAccount?> FindAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is not { } httpContext)
        {
            return null;
        }

        var result = await httpContext.AuthenticateAsync(CustomerPolicies.Scheme);

        if (!result.Succeeded
            || !Guid.TryParse(result.Principal.FindFirstValue(ShopForgeClaimTypes.StoreCustomerId), out var storeCustomerId)
            || !Guid.TryParse(result.Principal.FindFirstValue(ShopForgeClaimTypes.StoreId), out var storeId)
            || result.Principal.FindFirstValue(ClaimTypes.Email) is not { Length: > 0 } email)
        {
            return null;
        }

        return storeId == storeContext.StoreId ? new CurrentCustomerAccount(storeCustomerId, email) : null;
    }
}

internal sealed class CustomerUsage(DbContext dbContext) : ITenantUsage
{
    public async Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
    [
        new UsageCount("customers", await dbContext.Set<StoreCustomer>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .CountAsync(customer => storeIds.Contains(customer.StoreId), cancellationToken)),
    ];
}
