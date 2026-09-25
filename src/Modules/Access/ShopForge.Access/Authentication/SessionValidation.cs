using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;

namespace ShopForge.Access.Authentication;

internal static class SessionValidation
{
    // The cookie carries tenant and role for hours; checking them against the database on every admin request makes
    // deactivation and role changes effective immediately. Admin traffic is low, so one indexed lookup per request is cheap.
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } principal || !await IsUnchangedAsync(principal, context.HttpContext))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static async Task<bool> IsUnchangedAsync(ClaimsPrincipal principal, HttpContext httpContext)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        var current = await httpContext.RequestServices.GetRequiredService<DbContext>().Set<TenantUser>()
            .IgnoreQueryFilters()
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new { user.TenantId, user.Role })
            .SingleOrDefaultAsync(httpContext.RequestAborted);

        if (current is null
            || principal.FindFirstValue(ShopForgeClaimTypes.TenantId) != current.TenantId.ToString()
            || principal.FindFirstValue(ClaimTypes.Role) != current.Role.ToString())
        {
            return false;
        }

        // A suspended company's staff are out on their next request, not when their cookie happens to expire (D-104).
        return await httpContext.RequestServices.GetRequiredService<ITenantDirectory>()
            .IsActiveAsync(current.TenantId, httpContext.RequestAborted);
    }
}
