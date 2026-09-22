using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Security;

namespace ShopForge.Customers.Authentication;

internal static class CustomerSessions
{
    public static ClaimsPrincipal PrincipalFor(CustomerIdentity identity, StoreCustomer customer) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.Id.ToString()),
                new Claim(ClaimTypes.Email, identity.Email),
                new Claim(ShopForgeClaimTypes.StoreCustomerId, customer.Id.ToString()),
                new Claim(ShopForgeClaimTypes.StoreId, customer.StoreId.ToString()),
            ],
            CustomerPolicies.Scheme));

    // Authentication runs before the store is resolved, so the filters cannot help here: the session is checked
    // against the database by id, and the store on the cookie is matched to the resolved store later (CurrentCustomer).
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } principal || !await IsCurrentAsync(principal, context.HttpContext))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CustomerPolicies.Scheme);
        }
    }

    private static async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, HttpContext httpContext)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ShopForgeClaimTypes.StoreCustomerId), out var storeCustomerId)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var identityId))
        {
            return false;
        }

        var current = await httpContext.RequestServices.GetRequiredService<DbContext>().Set<StoreCustomer>()
            .IgnoreQueryFilters()
            .Where(customer => customer.Id == storeCustomerId && customer.CustomerIdentityId == identityId)
            .Select(customer => (Guid?)customer.StoreId)
            .SingleOrDefaultAsync(httpContext.RequestAborted);

        return current is not null && principal.FindFirstValue(ShopForgeClaimTypes.StoreId) == current.ToString();
    }
}
