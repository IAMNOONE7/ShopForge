using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Authentication;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Authentication);
        endpoints.MapPost("/logout", Logout);
        endpoints.MapGet("/me", GetCurrentUser).RequireAuthorization(AdminPolicies.TenantUser);
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<TenantUser> passwordHasher,
        StoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var email = TenantUser.NormalizeEmail(request.Email ?? "");

        // Authentication has to find the user before any tenant is known.
        var user = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(user => user.Email == email && user.IsActive, cancellationToken);

        var verification = user is null
            ? VerifyAgainstDummyHash(passwordHasher, request.Password)
            : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "");

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            storeContext.SetTenant(user.TenantId);
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ShopForgeClaimTypes.TenantId, user.TenantId.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return TypedResults.Ok(CurrentUserResponse.From(principal));
    }

    private static SignOutHttpResult Logout() =>
        TypedResults.SignOut(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]);

    private static Ok<CurrentUserResponse> GetCurrentUser(ClaimsPrincipal user) => TypedResults.Ok(CurrentUserResponse.From(user));

    // Hashing even for unknown e-mails keeps the response time from revealing which accounts exist.
    private static PasswordVerificationResult VerifyAgainstDummyHash(IPasswordHasher<TenantUser> passwordHasher, string? password)
    {
        passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password ?? "");

        return PasswordVerificationResult.Failed;
    }

    private static class DummyHash
    {
        public static readonly string Value = new PasswordHasher<TenantUser>().HashPassword(null!, Guid.NewGuid().ToString());
    }
}

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed record CurrentUserResponse(Guid Id, string Email, string Role, Guid TenantId)
{
    public static CurrentUserResponse From(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.FindFirstValue(ClaimTypes.Email)!,
        user.FindFirstValue(ClaimTypes.Role)!,
        Guid.Parse(user.FindFirstValue(ShopForgeClaimTypes.TenantId)!));
}
