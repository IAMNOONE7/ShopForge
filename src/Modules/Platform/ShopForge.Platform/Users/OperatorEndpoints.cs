using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Authentication;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Email;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Users;

// Running ShopForge should not depend on one person's mailbox, so an operator can bring in another the same way a
// company brings in a colleague. Nobody turns their own account off, which keeps at least one operator standing.
internal static class OperatorEndpoints
{
    public static void MapOperatorEndpoints(this IEndpointRouteBuilder platformOperator)
    {
        var operators = platformOperator.MapGroup("/operators");

        operators.MapGet("/", GetOperatorsAsync);
        operators.MapPost("/invitations", InviteAsync);
        operators.MapDelete("/invitations/{invitationId:guid}", WithdrawAsync);
        operators.MapPost("/{userId:guid}/deactivate", DeactivateAsync);
        operators.MapPost("/{userId:guid}/activate", ActivateAsync);

        var account = platformOperator.MapGroup("/account");

        account.MapPost("/password", ChangePasswordAsync);
        account.MapPost("/sign-out-everywhere", SignOutEverywhereAsync);
    }

    private static async Task<Ok<OperatorsResponse>> GetOperatorsAsync(DbContext dbContext, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var operators = await dbContext.Set<PlatformUser>()
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new OperatorResponse(user.Id, user.Email, user.IsActive))
            .ToListAsync(cancellationToken);

        var invitations = await dbContext.Set<PlatformInvitation>()
            .AsNoTracking()
            .Where(invitation => invitation.AcceptedAt == null && invitation.ExpiresAt > now)
            .OrderBy(invitation => invitation.Email)
            .Select(invitation => new OperatorInvitationResponse(invitation.Id, invitation.Email, invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new OperatorsResponse(operators, invitations));
    }

    private static async Task<Results<Ok<OperatorInvitationResponse>, ValidationProblem, ProblemHttpResult>> InviteAsync(
        InviteOperatorRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        PlatformMail mail,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(Emails.IsValid(request.Email), "email", "A valid e-mail address is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var email = PlatformUser.NormalizeEmail(request.Email!);

        if (await dbContext.Set<PlatformUser>().AnyAsync(user => user.Email == email, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That address already runs ShopForge");
        }

        var invitation = await mail.InviteAsync(email, caller.FindFirstValue(ClaimTypes.Email)!, clock.GetUtcNow(), cancellationToken);

        // Nothing about a company, so it belongs to no company: the platform's own record (D-116).
        audit.Record("operator.invited", email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new OperatorInvitationResponse(invitation.Id, invitation.Email, invitation.ExpiresAt));
    }

    private static async Task<Results<NoContent, NotFound>> WithdrawAsync(Guid invitationId, DbContext dbContext, CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Set<PlatformInvitation>()
            .SingleOrDefaultAsync(candidate => candidate.Id == invitationId && candidate.AcceptedAt == null, cancellationToken);

        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Remove(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<Results<Ok<OperatorResponse>, NotFound, ProblemHttpResult>> DeactivateAsync(
        Guid userId,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken) =>
        SetActiveAsync(userId, isActive: false, caller, dbContext, audit, cancellationToken);

    private static Task<Results<Ok<OperatorResponse>, NotFound, ProblemHttpResult>> ActivateAsync(
        Guid userId,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken) =>
        SetActiveAsync(userId, isActive: true, caller, dbContext, audit, cancellationToken);

    private static async Task<Results<Ok<OperatorResponse>, NotFound, ProblemHttpResult>> SetActiveAsync(
        Guid userId,
        bool isActive,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<PlatformUser>().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (Self(caller) == userId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot turn your own account off");
        }

        user.SetActive(isActive);

        // Somebody whose access is withdrawn is out of every browser they are in, not only out of the list.
        if (!isActive)
        {
            user.EndEverySession();
        }

        audit.Record(isActive ? "operator.activated" : "operator.deactivated", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new OperatorResponse(user.Id, user.Email, user.IsActive));
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ChangePasswordAsync(
        ChangeOperatorPasswordRequest request,
        ClaimsPrincipal caller,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<PlatformUser> passwordHasher,
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
        await httpContext.SignInAsync(PlatformPolicies.Scheme, PlatformSessions.PrincipalFor(user));

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
        await httpContext.SignInAsync(PlatformPolicies.Scheme, PlatformSessions.PrincipalFor(user));

        return TypedResults.NoContent();
    }

    private static Task<PlatformUser> CurrentAsync(ClaimsPrincipal caller, DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Set<PlatformUser>().SingleAsync(candidate => candidate.Id == Self(caller), cancellationToken);

    private static Guid Self(ClaimsPrincipal caller) => Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

internal sealed record InviteOperatorRequest(string? Email);

internal sealed record ChangeOperatorPasswordRequest(string? CurrentPassword, string? NewPassword);

internal sealed record OperatorsResponse(List<OperatorResponse> Operators, List<OperatorInvitationResponse> Invitations);

internal sealed record OperatorResponse(Guid Id, string Email, bool IsActive);

internal sealed record OperatorInvitationResponse(Guid Id, string Email, DateTimeOffset ExpiresAt);
