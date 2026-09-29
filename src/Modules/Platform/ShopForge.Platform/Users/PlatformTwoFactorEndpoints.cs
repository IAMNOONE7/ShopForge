using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Users;

// The same second factor a company's staff have, for the people who can see every company. Same shape on
// purpose: two-step enrolment, single-use recovery codes, and the password to turn it off (D-128, D-129).
internal static class PlatformTwoFactorEndpoints
{
    private const string Issuer = "ShopForge Platform";

    public static void MapPlatformTwoFactorEndpoints(this IEndpointRouteBuilder account)
    {
        var twoFactor = account.MapGroup("/two-factor");

        twoFactor.MapPost("/", BeginAsync);
        twoFactor.MapPost("/confirm", ConfirmAsync);
        twoFactor.MapPost("/off", TurnOffAsync);
    }

    private static async Task<Results<Ok<PlatformTwoFactorSetupResponse>, ProblemHttpResult>> BeginAsync(
        ClaimsPrincipal caller,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        if (user.IsTwoFactorEnabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A second factor is already on");
        }

        var secret = Totp.NewSecret();
        user.BeginTwoFactor(secret);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new PlatformTwoFactorSetupResponse(secret, Totp.EnrolmentUri(Issuer, user.Email, secret)));
    }

    private static async Task<Results<Ok<PlatformRecoveryCodesResponse>, ProblemHttpResult>> ConfirmAsync(
        PlatformTwoFactorCodeRequest request,
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
        dbContext.AddRange(codes.Select(code => new PlatformRecoveryCode(user.Id, TokenValues.Hash(code))));

        audit.Record("operator.two-factor-on", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new PlatformRecoveryCodesResponse(codes));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> TurnOffAsync(
        TurnOffPlatformTwoFactorRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(caller, dbContext, cancellationToken);

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That is not your password");
        }

        user.TurnOffTwoFactor();
        dbContext.RemoveRange(await ExistingCodesAsync(dbContext, user.Id, cancellationToken));
        audit.Record("operator.two-factor-off", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<List<PlatformRecoveryCode>> ExistingCodesAsync(DbContext dbContext, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Set<PlatformRecoveryCode>().Where(code => code.PlatformUserId == userId).ToListAsync(cancellationToken);

    private static Task<PlatformUser> CurrentAsync(ClaimsPrincipal caller, DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Set<PlatformUser>().SingleAsync(
            candidate => candidate.Id == Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);
}

internal sealed record PlatformTwoFactorCodeRequest(string? Code);

internal sealed record TurnOffPlatformTwoFactorRequest(string? Password);

internal sealed record PlatformTwoFactorSetupResponse(string Secret, string EnrolmentUri);

internal sealed record PlatformRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
