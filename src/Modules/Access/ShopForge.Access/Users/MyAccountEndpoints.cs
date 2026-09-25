using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Authentication;
using ShopForge.Access.Domain;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;

namespace ShopForge.Access.Users;

// What somebody does about their own account. Both of these end every other session they have; the browser doing
// the asking is signed in again on the way out, so it keeps working (D-113).
internal static class MyAccountEndpoints
{
    public static void MapMyAccountEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        var account = tenantAdmin.MapGroup("/account");

        account.MapPost("/password", ChangePasswordAsync);
        account.MapPost("/sign-out-everywhere", SignOutEverywhereAsync);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal caller,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<TenantUser> passwordHasher,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(request.NewPassword is { Length: >= 10 and <= 128 }, "newPassword", "The password needs at least 10 characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That is not your current password");
        }

        user.SetPasswordHash(passwordHasher.HashPassword(user, request.NewPassword!));
        user.EndEverySession();
        await dbContext.SaveChangesAsync(cancellationToken);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Sessions.PrincipalFor(user));

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SignOutEverywhereAsync(
        ClaimsPrincipal caller,
        HttpContext httpContext,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        user.EndEverySession();
        await dbContext.SaveChangesAsync(cancellationToken);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Sessions.PrincipalFor(user));

        return TypedResults.NoContent();
    }

    // The session was validated against this row on the way in, so it is there.
    private static Task<TenantUser> CurrentAsync(ClaimsPrincipal caller, DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Set<TenantUser>().SingleAsync(
            candidate => candidate.Id == Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);
}

internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
