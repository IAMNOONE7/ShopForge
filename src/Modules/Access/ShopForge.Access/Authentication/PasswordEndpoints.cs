using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Access.Users;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Authentication;

// Getting back in without being able to sign in first. Open, rate limited, and careful to say nothing about which
// addresses work here.
internal static class PasswordEndpoints
{
    public static void MapPasswordEndpoints(this IEndpointRouteBuilder auth)
    {
        var password = auth.MapGroup("/password").RequireRateLimiting(RateLimits.Authentication);

        password.MapPost("/forgot", ForgotAsync);
        password.MapPost("/reset", ResetAsync);
    }

    private static async Task<Accepted> ForgotAsync(
        ForgotPasswordRequest request,
        DbContext dbContext,
        StoreContext storeContext,
        ITenantDirectory tenants,
        PasswordMail mail,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var email = TenantUser.NormalizeEmail(request.Email ?? "");

        // Nobody is signed in, so the company is not known until the person is found.
        var user = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Email == email && candidate.IsActive, cancellationToken);

        if (user is not null && await tenants.IsActiveAsync(user.TenantId, cancellationToken))
        {
            storeContext.SetTenant(user.TenantId);
            await mail.SendAsync(user, clock.GetUtcNow(), cancellationToken);
        }

        // The same answer either way: whether an address works here is not something to hand out.
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetAsync(
        ResetPasswordRequest request,
        DbContext dbContext,
        StoreContext storeContext,
        IPasswordHasher<TenantUser> passwordHasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(request.Password is { Length: >= 10 and <= 128 }, "password", "The password needs at least 10 characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var now = clock.GetUtcNow();
        var hash = TokenValues.Hash(request.Token ?? "");
        var reset = await dbContext.Set<PasswordReset>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (reset?.IsUsable(now) != true)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "This link is no longer usable");
        }

        storeContext.SetTenant(reset.TenantId);
        var user = await dbContext.Set<TenantUser>().SingleAsync(candidate => candidate.Id == reset.TenantUserId, cancellationToken);

        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));

        // Whoever else was signed in as them is signed out: a reset is how somebody takes their account back (D-113).
        user.EndEverySession();
        reset.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}

internal sealed record ForgotPasswordRequest(string? Email);

internal sealed record ResetPasswordRequest(string? Token, string? Password);
