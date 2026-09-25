using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Authentication;

internal static class PlatformAuthEndpoints
{
    public static void MapPlatformAuth(this IEndpointRouteBuilder platform)
    {
        var auth = platform.MapGroup("/auth");

        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Authentication);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", GetCurrentUser).RequireAuthorization(PlatformPolicies.PlatformUser);
    }

    private static async Task<Results<Ok<PlatformUserResponse>, ProblemHttpResult>> LoginAsync(
        PlatformLoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
        CancellationToken cancellationToken)
    {
        var email = PlatformUser.NormalizeEmail(request.Email ?? "");
        var user = await dbContext.Set<PlatformUser>().SingleOrDefaultAsync(candidate => candidate.Email == email && candidate.IsActive, cancellationToken);
        var verification = user is null
            ? VerifyAgainstDummyHash(passwordHasher, request.Password)
            : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "");

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await httpContext.SignInAsync(PlatformPolicies.Scheme, PlatformSessions.PrincipalFor(user));

        return TypedResults.Ok(new PlatformUserResponse(user.Id, user.Email));
    }

    private static SignOutHttpResult Logout() => TypedResults.SignOut(authenticationSchemes: [PlatformPolicies.Scheme]);

    private static Ok<PlatformUserResponse> GetCurrentUser(ClaimsPrincipal user) =>
        TypedResults.Ok(new PlatformUserResponse(
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            user.FindFirstValue(ClaimTypes.Email)!));

    // Hashing even for unknown addresses keeps the response time from saying which operators exist.
    private static PasswordVerificationResult VerifyAgainstDummyHash(IPasswordHasher<PlatformUser> passwordHasher, string? password)
    {
        passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password ?? "");

        return PasswordVerificationResult.Failed;
    }

    private static class DummyHash
    {
        public static readonly string Value = new PasswordHasher<PlatformUser>().HashPassword(null!, Guid.NewGuid().ToString());
    }
}

internal sealed record PlatformLoginRequest(string? Email, string? Password);

internal sealed record PlatformUserResponse(Guid Id, string Email);
