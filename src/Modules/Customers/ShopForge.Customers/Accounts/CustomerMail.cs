using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Email;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Accounts;

internal sealed class CustomerMail(
    DbContext dbContext,
    IEmailSender emailSender,
    IStoreContext storeContext,
    ICurrentStoreSettings storeSettings,
    IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public Task SendVerificationAsync(CustomerIdentity identity, DateTimeOffset now, CancellationToken cancellationToken) =>
        SendWithTokenAsync(
            identity,
            CustomerTokenPurpose.EmailVerification,
            now + VerificationLifetime,
            now,
            "account/verify",
            (store, link) => ($"Confirm your e-mail address for {store}",
                $"Welcome to {store}. Confirm your e-mail address to finish creating your account: {link}"),
            cancellationToken);

    public Task SendPasswordResetAsync(CustomerIdentity identity, DateTimeOffset now, CancellationToken cancellationToken) =>
        SendWithTokenAsync(
            identity,
            CustomerTokenPurpose.PasswordReset,
            now + ResetLifetime,
            now,
            "account/reset-password",
            (store, link) => ($"Reset your {store} password",
                $"Use this link within an hour to choose a new password: {link}. If you did not ask for it, you can ignore this e-mail."),
            cancellationToken);

    public async Task SendAccountExistsAsync(CustomerIdentity identity, CancellationToken cancellationToken)
    {
        var store = (await storeSettings.GetAsync(cancellationToken)).Name;

        await emailSender.SendAsync(
            new EmailMessage(
                identity.Email,
                $"You already have a {store} account",
                $"Someone tried to register this address at {store}. Sign in instead, or reset your password at {Link("account/forgot-password", token: null)}."),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SendWithTokenAsync(
        CustomerIdentity identity,
        CustomerTokenPurpose purpose,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string path,
        Func<string, string, (string Subject, string Body)> compose,
        CancellationToken cancellationToken)
    {
        // Only the newest link of a kind works; asking again invalidates the previous one. The store filter keeps
        // that to this store's links: asking here must not put out a link another store sent (D-102).
        var outstanding = await dbContext.Set<CustomerToken>()
            .Where(token => token.CustomerIdentityId == identity.Id && token.Purpose == purpose && token.UsedAt == null)
            .ToListAsync(cancellationToken);

        outstanding.ForEach(token => token.Use(now));

        var (value, hash) = TokenValues.Create();
        dbContext.Add(new CustomerToken(identity.TenantId, identity.Id, storeContext.StoreId!.Value, purpose, hash, expiresAt));

        var store = (await storeSettings.GetAsync(cancellationToken)).Name;
        var (subject, body) = compose(store, Link(path, value));

        // Sending is enqueued, so it has to happen before the save that makes the token real: the message and the
        // token it points at are written together or not at all (D-065).
        await emailSender.SendAsync(new EmailMessage(identity.Email, subject, body), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string Link(string path, string? token)
    {
        var request = httpContextAccessor.HttpContext!.Request;
        var query = token is null ? string.Empty : $"?token={Uri.EscapeDataString(token)}";

        return $"{request.Scheme}://{request.Host}/{path}{query}";
    }
}
