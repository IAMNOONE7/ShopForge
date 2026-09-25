using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Email;
using ShopForge.Shared.Security;

namespace ShopForge.Platform.Users;

// Mail to somebody who runs ShopForge belongs to no company and no shop, so it is the one kind that carries no
// scope at all (D-111).
internal sealed class PlatformMail(DbContext dbContext, IEmailSender emailSender, IHttpContextAccessor httpContextAccessor)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public async Task<PlatformInvitation> InviteAsync(string email, string invitedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outstanding = await dbContext.Set<PlatformInvitation>()
            .Where(invitation => invitation.Email == email && invitation.AcceptedAt == null)
            .ToListAsync(cancellationToken);

        dbContext.RemoveRange(outstanding);

        var (value, hash) = TokenValues.Create();
        var invitation = new PlatformInvitation(email, hash, now, now + InvitationLifetime);
        dbContext.Add(invitation);

        await emailSender.SendOutsideStoreAsync(
            new EmailMessage(
                email,
                "You have been invited to run ShopForge",
                $"{invitedBy} invited you to administer ShopForge itself. Choose a password to accept: {Link("platform/invitations/accept", value)}. "
                + $"The invitation expires in {InvitationLifetime.Days} days."),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return invitation;
    }

    public async Task SendResetAsync(PlatformUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outstanding = await dbContext.Set<PlatformPasswordReset>()
            .Where(reset => reset.PlatformUserId == user.Id && reset.UsedAt == null)
            .ToListAsync(cancellationToken);

        outstanding.ForEach(reset => reset.Use(now));

        var (value, hash) = TokenValues.Create();
        dbContext.Add(new PlatformPasswordReset(user.Id, hash, now, now + ResetLifetime));

        await emailSender.SendOutsideStoreAsync(
            new EmailMessage(
                user.Email,
                "Reset your ShopForge operator password",
                $"Use this link within an hour to choose a new password: {Link("platform/reset-password", value)}. "
                + "If you did not ask for it, you can ignore this e-mail."),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string Link(string path, string token)
    {
        var request = httpContextAccessor.HttpContext!.Request;

        return $"{request.Scheme}://{request.Host}/{path}?token={Uri.EscapeDataString(token)}";
    }
}
