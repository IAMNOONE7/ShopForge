using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Authentication;

internal static class PlatformSessions
{
    public static ClaimsPrincipal PrincipalFor(PlatformUser user) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            PlatformPolicies.Scheme));

    // Checked against the database on every request, as admin sessions are (D-038): withdrawing an operator's
    // access has to take effect now, not in eight hours.
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } principal || !await IsCurrentAsync(principal, context.HttpContext))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(PlatformPolicies.Scheme);
        }
    }

    private static async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, HttpContext httpContext)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        return await httpContext.RequestServices.GetRequiredService<DbContext>().Set<PlatformUser>()
            .AnyAsync(user => user.Id == userId && user.IsActive, httpContext.RequestAborted);
    }
}
