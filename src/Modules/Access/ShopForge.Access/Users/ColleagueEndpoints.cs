using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Email;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Users;

// Who works on a company, and how they get there. Two rules keep a company from locking itself out or growing an
// owner it did not mean to: an Owner is only ever changed or created by another Owner, and nobody changes their own
// role or turns themselves off. Together those make the last Owner impossible to remove (D-112).
internal static class ColleagueEndpoints
{
    public static void MapColleagueEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        var users = tenantAdmin.MapGroup("/users").RequireAuthorization(AdminPolicies.StoreManagement);

        users.MapGet("/", GetColleaguesAsync);
        users.MapPost("/invitations", InviteAsync);
        users.MapDelete("/invitations/{invitationId:guid}", WithdrawAsync);
        users.MapPut("/{userId:guid}/role", ChangeRoleAsync);
        users.MapPost("/{userId:guid}/deactivate", DeactivateAsync);
        users.MapPost("/{userId:guid}/activate", ActivateAsync);
    }

    private static async Task<Ok<ColleaguesResponse>> GetColleaguesAsync(
        DbContext dbContext,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var users = await dbContext.Set<TenantUser>()
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new ColleagueResponse(user.Id, user.Email, user.Role.ToString(), user.IsActive))
            .ToListAsync(cancellationToken);

        var invitations = await dbContext.Set<TenantInvitation>()
            .AsNoTracking()
            .Where(invitation => invitation.AcceptedAt == null && invitation.ExpiresAt > now)
            .OrderBy(invitation => invitation.Email)
            .Select(invitation => new InvitationResponse(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ColleaguesResponse(users, invitations));
    }

    private static async Task<Results<Ok<InvitationResponse>, ValidationProblem, ProblemHttpResult>> InviteAsync(
        InviteRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IStoreContext storeContext,
        InvitationMail mail,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var role = ParseRole(request.Role);
        var errors = new RequestErrors()
            .Check(Emails.IsValid(request.Email), "email", "A valid e-mail address is required.")
            .Check(role is not null, "role", "That is not a role.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (role == TenantRole.Owner && !IsOwner(caller))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an owner invites another owner");
        }

        // The e-mail identifies the person when they sign in, so it can belong to one company only; the filter is
        // lifted to say so across all of them rather than letting the unique index fail the request.
        var email = TenantUser.NormalizeEmail(request.Email!);
        var taken = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email, cancellationToken);

        if (taken)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That address already belongs to somebody on ShopForge");
        }

        var invitation = await mail.SendAsync(
            storeContext.TenantId!.Value,
            email,
            role!.Value,
            caller.FindFirstValue(ClaimTypes.Email)!,
            clock.GetUtcNow(),
            cancellationToken);

        audit.Record("colleague.invited", email, new { role = role!.Value.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new InvitationResponse(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.ExpiresAt));
    }

    private static async Task<Results<NoContent, NotFound>> WithdrawAsync(
        Guid invitationId,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Set<TenantInvitation>()
            .SingleOrDefaultAsync(candidate => candidate.Id == invitationId && candidate.AcceptedAt == null, cancellationToken);

        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        // An invitation nobody took up is not history worth keeping.
        dbContext.Remove(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ColleagueResponse>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeRoleAsync(
        Guid userId,
        RoleRequest request,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var role = ParseRole(request.Role);
        var errors = new RequestErrors().Check(role is not null, "role", "That is not a role.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var user = await FindAsync(dbContext, userId, cancellationToken);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (IsSelf(caller, userId))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot change your own role");
        }

        if ((role == TenantRole.Owner || user.Role == TenantRole.Owner) && !IsOwner(caller))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an owner changes an owner");
        }

        var was = user.Role;
        user.ChangeRole(role!.Value);
        audit.Record("colleague.role-changed", user.Email, new { from = was.ToString(), to = user.Role.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ColleagueResponse(user.Id, user.Email, user.Role.ToString(), user.IsActive));
    }

    private static Task<Results<Ok<ColleagueResponse>, NotFound, ProblemHttpResult>> DeactivateAsync(
        Guid userId,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken) =>
        SetActiveAsync(userId, isActive: false, caller, dbContext, audit, cancellationToken);

    private static Task<Results<Ok<ColleagueResponse>, NotFound, ProblemHttpResult>> ActivateAsync(
        Guid userId,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken) =>
        SetActiveAsync(userId, isActive: true, caller, dbContext, audit, cancellationToken);

    private static async Task<Results<Ok<ColleagueResponse>, NotFound, ProblemHttpResult>> SetActiveAsync(
        Guid userId,
        bool isActive,
        ClaimsPrincipal caller,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await FindAsync(dbContext, userId, cancellationToken);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (IsSelf(caller, userId))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot turn your own account off");
        }

        if (user.Role == TenantRole.Owner && !IsOwner(caller))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an owner changes an owner");
        }

        // People are kept and switched off, never deleted: orders, invoices and shipments name them (D-112).
        user.SetActive(isActive);
        audit.Record(isActive ? "colleague.activated" : "colleague.deactivated", user.Email);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ColleagueResponse(user.Id, user.Email, user.Role.ToString(), user.IsActive));
    }

    private static Task<TenantUser?> FindAsync(DbContext dbContext, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Set<TenantUser>().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

    private static bool IsSelf(ClaimsPrincipal caller, Guid userId) =>
        caller.FindFirstValue(ClaimTypes.NameIdentifier) == userId.ToString();

    private static bool IsOwner(ClaimsPrincipal caller) => caller.IsInRole(nameof(TenantRole.Owner));

    private static TenantRole? ParseRole(string? role) =>
        Enum.TryParse<TenantRole>(role, ignoreCase: false, out var parsed) ? parsed : null;
}

internal sealed record InviteRequest(string? Email, string? Role);

internal sealed record RoleRequest(string? Role);

internal sealed record ColleaguesResponse(List<ColleagueResponse> Users, List<InvitationResponse> Invitations);

internal sealed record ColleagueResponse(Guid Id, string Email, string Role, bool IsActive);

internal sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);
