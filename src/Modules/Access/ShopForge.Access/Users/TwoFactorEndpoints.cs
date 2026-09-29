using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;

namespace ShopForge.Access.Users;

// Turning a second factor on, and off again. Enrolment is two steps on purpose: the secret is stored when it is
// shown, and only counts once a code has come back, so nobody locks themselves out of a company by closing the
// tab halfway (D-128).
internal static class TwoFactorEndpoints
{
    private const string Issuer = "ShopForge";

    public static void MapTwoFactorEndpoints(this IEndpointRouteBuilder account)
    {
        var twoFactor = account.MapGroup("/two-factor");

        twoFactor.MapPost("/", BeginAsync);
        twoFactor.MapPost("/confirm", ConfirmAsync);

        // Turning it off takes the password, so it is a post like the other things that need proof rather than a
        // delete with a body nobody can agree how to send.
        twoFactor.MapPost("/off", TurnOffAsync);
    }

    private static async Task<Results<Ok<TwoFactorSetupResponse>, ProblemHttpResult>> BeginAsync(
        ClaimsPrincipal caller,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        if (user.IsTwoFactorEnabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A second factor is already on");
        }

        // Starting again replaces whatever was half-set-up before, so an abandoned attempt cannot be finished by
        // somebody who saw the first screen.
        var secret = Totp.NewSecret();
        user.BeginTwoFactor(secret);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new TwoFactorSetupResponse(secret, Totp.EnrolmentUri(Issuer, user.Email, secret)));
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> ConfirmAsync(
        TwoFactorCodeRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);
        var now = clock.GetUtcNow();

        if (user.TwoFactorSecret is not { } secret)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nothing has been set up to confirm");
        }

        if (!Totp.IsValid(secret, request.Code, now))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That code is not right");
        }

        user.ConfirmTwoFactor(now);

        var codes = RecoveryCodes.Issue();
        dbContext.RemoveRange(await ExistingCodesAsync(dbContext, user.Id, cancellationToken));
        dbContext.AddRange(codes.Select(code => new RecoveryCode(user.TenantId, user.Id, TokenValues.Hash(code))));

        audit.Record("colleague.two-factor-on", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Shown once. They are hashed here, so nobody — including us — can read them back.
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> TurnOffAsync(
        TurnOffTwoFactorRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IPasswordHasher<TenantUser> passwordHasher,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        // Turning protection off is exactly what somebody who has stolen a session would do first.
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That is not your password");
        }

        user.TurnOffTwoFactor();
        dbContext.RemoveRange(await ExistingCodesAsync(dbContext, user.Id, cancellationToken));
        audit.Record("colleague.two-factor-off", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<List<RecoveryCode>> ExistingCodesAsync(DbContext dbContext, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Set<RecoveryCode>().Where(code => code.TenantUserId == userId).ToListAsync(cancellationToken);

    private static Task<TenantUser> CurrentAsync(ClaimsPrincipal caller, DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Set<TenantUser>().SingleAsync(
            candidate => candidate.Id == Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);
}

internal sealed record TwoFactorCodeRequest(string? Code);

internal sealed record TurnOffTwoFactorRequest(string? Password);

internal sealed record TwoFactorSetupResponse(string Secret, string EnrolmentUri);

internal sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
