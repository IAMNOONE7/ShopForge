using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Users;

// Taking up an invitation happens before there is anybody to sign in as, so these are open, rate limited, and say
// as little as possible about links that do not work.
internal static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var invitations = endpoints.MapGroup("/invitations").RequireRateLimiting(RateLimits.Authentication);

        invitations.MapGet("/{token}", GetInvitationAsync);
        invitations.MapPost("/accept", AcceptAsync);
    }

    private static async Task<Results<Ok<OpenInvitationResponse>, NotFound>> GetInvitationAsync(
        string token,
        DbContext dbContext,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invitation = await FindAsync(dbContext, token, clock, cancellationToken);

        return invitation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new OpenInvitationResponse(invitation.Email, invitation.Role.ToString()));
    }

    private static async Task<Results<Ok<AcceptedInvitationResponse>, ValidationProblem, ProblemHttpResult>> AcceptAsync(
        AcceptInvitationRequest request,
        DbContext dbContext,
        StoreContext storeContext,
        ITenantDirectory tenants,
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
        var invitation = await FindAsync(dbContext, request.Token ?? "", clock, cancellationToken);

        if (invitation is null || !await tenants.IsActiveAsync(invitation.TenantId, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "This invitation is no longer open");
        }

        // Everything from here belongs to the company that sent the invitation, and the save guard holds it there.
        storeContext.SetTenant(invitation.TenantId);

        var taken = await dbContext.Set<TenantUser>()
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == invitation.Email, cancellationToken);

        if (taken)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That address already belongs to somebody on ShopForge");
        }

        var user = new TenantUser(invitation.TenantId, invitation.Email, invitation.Role);
        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password!));
        dbContext.Add(user);
        invitation.Accept(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AcceptedInvitationResponse(user.Email));
    }

    // The tenant is unknown until the link is found, so the filter comes off and the hash is what scopes the lookup.
    private static async Task<TenantInvitation?> FindAsync(DbContext dbContext, string token, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = TokenValues.Hash(token);
        var invitation = await dbContext.Set<TenantInvitation>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        return invitation?.IsUsable(clock.GetUtcNow()) == true ? invitation : null;
    }
}

internal sealed record AcceptInvitationRequest(string? Token, string? Password);

internal sealed record OpenInvitationResponse(string Email, string Role);

internal sealed record AcceptedInvitationResponse(string Email);
