using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Accounts;

internal sealed class CurrentCustomer(IHttpContextAccessor httpContextAccessor, IStoreContext storeContext) : ICurrentCustomer
{
    // A session only counts for the store it was created in, whatever host the cookie arrived on.
    public async Task<Guid?> FindStoreCustomerIdAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is not { } httpContext)
        {
            return null;
        }

        var result = await httpContext.AuthenticateAsync(CustomerPolicies.Scheme);

        if (!result.Succeeded
            || !Guid.TryParse(result.Principal.FindFirstValue(ShopForgeClaimTypes.StoreCustomerId), out var storeCustomerId)
            || !Guid.TryParse(result.Principal.FindFirstValue(ShopForgeClaimTypes.StoreId), out var storeId))
        {
            return null;
        }

        return storeId == storeContext.StoreId ? storeCustomerId : null;
    }
}
