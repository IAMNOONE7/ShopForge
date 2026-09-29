using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
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
        auth.MapPost("/two-factor", CompleteTwoFactorAsync).RequireRateLimiting(RateLimits.Authentication);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", GetCurrentUserAsync).RequireAuthorization(PlatformPolicies.PlatformUser);
    }

    private static async Task<Results<Ok<PlatformSignInResponse>, ProblemHttpResult>> LoginAsync(
        PlatformLoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
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

        // Somebody who can see every company should not be one password away from all of them (D-129).
        if (user.IsTwoFactorEnabled)
        {
            return TypedResults.Ok(PlatformSignInResponse.NeedsCode(TwoFactorTickets.Issue(
                dataProtection, TwoFactorTickets.PlatformUser, user.Id, user.SecurityStamp, clock.GetUtcNow())));
        }

        await httpContext.SignInAsync(PlatformPolicies.Scheme, PlatformSessions.PrincipalFor(user));

        return TypedResults.Ok(PlatformSignInResponse.SignedIn(new PlatformUserResponse(user.Id, user.Email, user.IsTwoFactorEnabled)));
    }

    private static async Task<Results<Ok<PlatformSignInResponse>, ProblemHttpResult>> CompleteTwoFactorAsync(
        PlatformTwoFactorRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // A ticket earned as a company's staff cannot be read here: the two are protected under different
        // purposes, so the key itself refuses rather than a check remembering to (D-129).
        if (TwoFactorTickets.Read(dataProtection, TwoFactorTickets.PlatformUser, request.Ticket) is not { } pending)
        {
            return Refused();
        }

        var user = await dbContext.Set<PlatformUser>()
            .SingleOrDefaultAsync(candidate => candidate.Id == pending.UserId && candidate.IsActive, cancellationToken);

        if (user is null || user.SecurityStamp != pending.SecurityStamp || user.TwoFactorSecret is not { } secret)
        {
            return Refused();
        }

        if (!Totp.IsValid(secret, request.Code, now) && !await SpendRecoveryCodeAsync(dbContext, user, request.Code, now, cancellationToken))
        {
            return Refused();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await httpContext.SignInAsync(PlatformPolicies.Scheme, PlatformSessions.PrincipalFor(user));

        return TypedResults.Ok(PlatformSignInResponse.SignedIn(new PlatformUserResponse(user.Id, user.Email, user.IsTwoFactorEnabled)));
    }

    private static async Task<bool> SpendRecoveryCodeAsync(
        DbContext dbContext,
        PlatformUser user,
        string? code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var hash = TokenValues.Hash(code.Trim().ToUpperInvariant());
        var recovery = await dbContext.Set<PlatformRecoveryCode>()
            .SingleOrDefaultAsync(candidate => candidate.PlatformUserId == user.Id && candidate.CodeHash == hash && candidate.UsedAt == null, cancellationToken);

        recovery?.Use(now);

        return recovery is not null;
    }

    private static ProblemHttpResult Refused() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "That code is not right");

    private static SignOutHttpResult Logout() => TypedResults.SignOut(authenticationSchemes: [PlatformPolicies.Scheme]);

    private static async Task<Ok<PlatformUserResponse>> GetCurrentUserAsync(
        ClaimsPrincipal user,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var enabled = await dbContext.Set<PlatformUser>()
            .AnyAsync(candidate => candidate.Id == id && candidate.TwoFactorEnabledAt != null, cancellationToken);

        return TypedResults.Ok(new PlatformUserResponse(id, user.FindFirstValue(ClaimTypes.Email)!, enabled));
    }

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

internal sealed record PlatformUserResponse(Guid Id, string Email, bool IsTwoFactorEnabled = false);

internal sealed record PlatformTwoFactorRequest(string? Ticket, string? Code);

internal sealed record PlatformSignInResponse(bool TwoFactorRequired, string? Ticket, PlatformUserResponse? User)
{
    public static PlatformSignInResponse NeedsCode(string ticket) => new(true, ticket, null);

    public static PlatformSignInResponse SignedIn(PlatformUserResponse user) => new(false, null, user);
}
