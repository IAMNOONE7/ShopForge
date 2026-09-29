using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Authentication;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Authentication);
        endpoints.MapPost("/two-factor", CompleteTwoFactorAsync).RequireRateLimiting(RateLimits.Authentication);
        endpoints.MapPost("/logout", Logout);
        endpoints.MapGet("/me", GetCurrentUserAsync).RequireAuthorization(AdminPolicies.TenantUser);
    }

    private static async Task<Results<Ok<SignInResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<TenantUser> passwordHasher,
        StoreContext storeContext,
        ITenantDirectory tenants,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
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

        // A suspended company cannot be administered either, and its staff are told what is wrong rather than
        // being left to think they mistyped (D-104).
        if (!await tenants.IsActiveAsync(user.TenantId, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This account is suspended",
                detail: "Get in touch with ShopForge to have it opened again.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            storeContext.SetTenant(user.TenantId);
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // The password was right, which is half of it. Nothing is signed in until the code comes back, and the
        // ticket that carries the half-finished sign-in expires in five minutes and names the session stamp, so a
        // password changed in between makes it worthless (D-128).
        if (user.IsTwoFactorEnabled)
        {
            return TypedResults.Ok(SignInResponse.NeedsCode(
                TwoFactorTickets.Issue(dataProtection, user.Id, user.SecurityStamp, clock.GetUtcNow())));
        }

        var principal = Sessions.PrincipalFor(user);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return TypedResults.Ok(SignInResponse.SignedIn(CurrentUserResponse.From(principal)));
    }

    private static async Task<Results<Ok<SignInResponse>, ProblemHttpResult>> CompleteTwoFactorAsync(
        TwoFactorRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        StoreContext storeContext,
        ITenantDirectory tenants,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (TwoFactorTickets.Read(dataProtection, request.Ticket) is not { } pending)
        {
            return Refused();
        }

        var user = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == pending.UserId && candidate.IsActive, cancellationToken);

        if (user is null
            || user.SecurityStamp != pending.SecurityStamp
            || user.TwoFactorSecret is not { } secret
            || !await tenants.IsActiveAsync(user.TenantId, cancellationToken))
        {
            return Refused();
        }

        storeContext.SetTenant(user.TenantId);

        if (!Totp.IsValid(secret, request.Code, now) && !await SpendRecoveryCodeAsync(dbContext, user, request.Code, now, cancellationToken))
        {
            return Refused();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var principal = Sessions.PrincipalFor(user);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return TypedResults.Ok(SignInResponse.SignedIn(CurrentUserResponse.From(principal)));
    }

    // A recovery code works once and is then gone, which is the whole of what makes it safe to write down.
    private static async Task<bool> SpendRecoveryCodeAsync(
        DbContext dbContext,
        TenantUser user,
        string? code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var hash = TokenValues.Hash(code.Trim().ToUpperInvariant());
        var recovery = await dbContext.Set<RecoveryCode>()
            .SingleOrDefaultAsync(candidate => candidate.TenantUserId == user.Id && candidate.CodeHash == hash && candidate.UsedAt == null, cancellationToken);

        recovery?.Use(now);

        return recovery is not null;
    }

    private static ProblemHttpResult Refused() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "That code is not right");

    private static SignOutHttpResult Logout() =>
        TypedResults.SignOut(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]);

    // Read rather than taken from the cookie, because the admin uses it to ask somebody to turn a second factor
    // on and the answer has to be current.
    private static async Task<Ok<CurrentUserResponse>> GetCurrentUserAsync(
        ClaimsPrincipal user,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        // This endpoint sits beside signing in rather than inside the company's own scope, so there is no tenant
        // to filter by; the id comes from a cookie that has already been checked against the row (D-038).
        var enabled = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .AnyAsync(
                candidate => candidate.Id == Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)
                    && candidate.TwoFactorEnabledAt != null,
                cancellationToken);

        return TypedResults.Ok(CurrentUserResponse.From(user) with { IsTwoFactorEnabled = enabled });
    }

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

internal sealed record TwoFactorRequest(string? Ticket, string? Code);

// One shape for both answers, so a client always reads the same thing back from signing in.
internal sealed record SignInResponse(bool TwoFactorRequired, string? Ticket, CurrentUserResponse? User)
{
    public static SignInResponse NeedsCode(string ticket) => new(true, ticket, null);

    public static SignInResponse SignedIn(CurrentUserResponse user) => new(false, null, user);
}

internal sealed record CurrentUserResponse(Guid Id, string Email, string Role, Guid TenantId, bool IsTwoFactorEnabled = false)
{
    public static CurrentUserResponse From(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.FindFirstValue(ClaimTypes.Email)!,
        user.FindFirstValue(ClaimTypes.Role)!,
        Guid.Parse(user.FindFirstValue(ShopForgeClaimTypes.TenantId)!));
}
