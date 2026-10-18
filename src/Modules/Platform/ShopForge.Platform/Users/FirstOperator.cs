using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Email;

namespace ShopForge.Platform.Users;

// The first person to run a new deployment, invited the way every later operator is: a link by mail and a password of
// their own choosing, nothing secret in the deployment's hands (D-208). The environment names them, and every release
// sends a fresh invitation until somebody runs the platform, so a lost letter is one release, or one start of the
// migration job, away from a new one. After that, operators invite each other through the platform's API.
public static class FirstOperator
{
    public static async Task<FirstOperatorInvitation> InviteFirstOperatorAsync(
        this IServiceProvider services, string email, Uri? origin, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        if (await dbContext.Set<PlatformUser>().AnyAsync(cancellationToken))
        {
            return new(Refused: false, "The platform has an operator, so nobody was invited.");
        }

        if (!Emails.IsValid(email))
        {
            return new(Refused: true, $"'{email}' is not an e-mail address.");
        }

        if (origin is null)
        {
            return new(Refused: true, "There is no admin host for the invitation's link to point at.");
        }

        // With nobody to invite anybody else, every open invitation is an earlier one of these, and the new one replaces
        // them all, including one to an address the environment no longer names.
        var normalized = PlatformUser.NormalizeEmail(email);
        dbContext.RemoveRange(await dbContext.Set<PlatformInvitation>().Where(invitation => invitation.AcceptedAt == null).ToListAsync(cancellationToken));
        await scope.ServiceProvider.GetRequiredService<PlatformMail>()
            .InviteAsync(normalized, "The deployment", clock.GetUtcNow(), cancellationToken, origin);

        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("operator.invited", normalized);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(Refused: false, "Invited the platform's first operator; the invitation goes out by mail.");
    }
}

public sealed record FirstOperatorInvitation(bool Refused, string Outcome);
