using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Email;
using ShopForge.Shared.Security;

namespace ShopForge.Access.Users;

internal sealed class PasswordMail(DbContext dbContext, IEmailSender emailSender, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public async Task SendAsync(TenantUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Only the newest link works; asking twice puts the first one out.
        var outstanding = await dbContext.Set<PasswordReset>()
            .Where(reset => reset.TenantUserId == user.Id && reset.UsedAt == null)
            .ToListAsync(cancellationToken);

        outstanding.ForEach(reset => reset.Use(now));

        var (value, hash) = TokenValues.Create();
        dbContext.Add(new PasswordReset(user.TenantId, user.Id, hash, now, now + Lifetime));

        // Staff mail belongs to the company, not to one of its shops (D-111), and goes out with the token that it
        // points at or not at all (D-065).
        await emailSender.SendOutsideStoreAsync(
            new EmailMessage(
                user.Email,
                "Reset your ShopForge password",
                $"Use this link within an hour to choose a new password: {Link(value)}. "
                + "If you did not ask for it, you can ignore this e-mail."),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string Link(string token)
    {
        var request = httpContextAccessor.HttpContext!.Request;

        return $"{request.Scheme}://{request.Host}/reset-password?token={Uri.EscapeDataString(token)}";
    }
}
