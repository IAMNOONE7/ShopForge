using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Email;
using ShopForge.Shared.Security;

namespace ShopForge.Access.Users;

internal sealed class InvitationMail(DbContext dbContext, IEmailSender emailSender, IHttpContextAccessor httpContextAccessor)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<TenantInvitation> SendAsync(Guid tenantId, string email, TenantRole role, string invitedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Only the newest invitation to an address works; inviting again puts the previous link out.
        var outstanding = await dbContext.Set<TenantInvitation>()
            .Where(invitation => invitation.Email == email && invitation.AcceptedAt == null)
            .ToListAsync(cancellationToken);

        dbContext.RemoveRange(outstanding);

        var (value, hash) = TokenValues.Create();
        var invitation = new TenantInvitation(tenantId, email, role, hash, now, now + Lifetime);
        dbContext.Add(invitation);

        // An invitation belongs to a company, not to one of its shops, so it cannot travel as a shop's message
        // (D-111). Enqueueing it before the save keeps the mail and the invitation it points at together (D-065).
        await emailSender.SendOutsideStoreAsync(
            new EmailMessage(
                email,
                "You have been invited to ShopForge",
                $"{invitedBy} invited you to help run their ShopForge account as {role}. Choose a password to accept: "
                + $"{Link(value)}. The invitation expires in {Lifetime.Days} days."),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return invitation;
    }

    private string Link(string token)
    {
        var request = httpContextAccessor.HttpContext!.Request;

        return $"{request.Scheme}://{request.Host}/invitations/accept?token={Uri.EscapeDataString(token)}";
    }
}
