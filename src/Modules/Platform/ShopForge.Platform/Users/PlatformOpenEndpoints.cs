using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Users;

// Taking up an invitation, and getting back in after forgetting a password: both happen before there is anybody to
// sign in as. Open, rate limited, and quiet about which addresses exist.
internal static class PlatformOpenEndpoints
{
    public static void MapPlatformOpenEndpoints(this IEndpointRouteBuilder platform)
    {
        var invitations = platform.MapGroup("/invitations").RequireRateLimiting(RateLimits.Authentication);

        invitations.MapPost("/details", GetInvitationAsync);
        invitations.MapPost("/accept", AcceptAsync);

        var password = platform.MapGroup("/auth/password").RequireRateLimiting(RateLimits.Authentication);

        password.MapPost("/forgot", ForgotAsync);
        password.MapPost("/reset", ResetAsync);
    }

    // The token travels in the body, not the route, so it never reaches a log line or a span (D-119).
    private static async Task<Results<Ok<OpenOperatorInvitationResponse>, NotFound>> GetInvitationAsync(
        OperatorInvitationLookupRequest request,
        DbContext dbContext,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invitation = await FindInvitationAsync(dbContext, request.Token ?? "", clock, cancellationToken);

        return invitation is null ? TypedResults.NotFound() : TypedResults.Ok(new OpenOperatorInvitationResponse(invitation.Email));
    }

    private static async Task<Results<Ok<OperatorResponse>, ValidationProblem, ProblemHttpResult>> AcceptAsync(
        AcceptOperatorInvitationRequest request,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
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
        var invitation = await FindInvitationAsync(dbContext, request.Token ?? "", clock, cancellationToken);

        if (invitation is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "This invitation is no longer open");
        }

        if (await dbContext.Set<PlatformUser>().AnyAsync(candidate => candidate.Email == invitation.Email, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That address already runs ShopForge");
        }

        var user = new PlatformUser(invitation.Email);
        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
        dbContext.Add(user);
        invitation.Accept(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new OperatorResponse(user.Id, user.Email, user.IsActive));
    }

    private static async Task<Accepted> ForgotAsync(
        ForgotOperatorPasswordRequest request,
        DbContext dbContext,
        PlatformMail mail,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var email = PlatformUser.NormalizeEmail(request.Email ?? "");
        var user = await dbContext.Set<PlatformUser>()
            .SingleOrDefaultAsync(candidate => candidate.Email == email && candidate.IsActive, cancellationToken);

        if (user is not null)
        {
            await mail.SendResetAsync(user, clock.GetUtcNow(), cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetAsync(
        ResetOperatorPasswordRequest request,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
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
        var reset = await dbContext.Set<PlatformPasswordReset>().SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (reset?.IsUsable(now) != true)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "This link is no longer usable");
        }

        var user = await dbContext.Set<PlatformUser>().SingleAsync(candidate => candidate.Id == reset.PlatformUserId, cancellationToken);

        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
        user.EndEverySession();
        reset.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<PlatformInvitation?> FindInvitationAsync(
        DbContext dbContext,
        string token,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = TokenValues.Hash(token);
        var invitation = await dbContext.Set<PlatformInvitation>().SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        return invitation?.IsUsable(clock.GetUtcNow()) == true ? invitation : null;
    }
}

internal sealed record OperatorInvitationLookupRequest(string? Token);

internal sealed record AcceptOperatorInvitationRequest(string? Token, string? Password);

internal sealed record ForgotOperatorPasswordRequest(string? Email);

internal sealed record ResetOperatorPasswordRequest(string? Token, string? Password);

internal sealed record OpenOperatorInvitationResponse(string Email);
