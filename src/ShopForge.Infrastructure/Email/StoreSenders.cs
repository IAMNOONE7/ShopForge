using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Email;

// Which domain a message goes out through, with which key, from which address (D-211).
internal interface IMailSenders
{
    Task<MailSender> CurrentAsync(CancellationToken cancellationToken);
}

internal sealed record MailSender(string Domain, string Key, string Address, string? ReplyTo);

// A store whose own domain is active sends through it with its own key; any other store, and anything that belongs to
// no store, through the platform's. A store whose key has gone missing from the vault falls back too, loudly: a
// customer still gets their receipt, and the log says why it came from the platform.
internal sealed class StoreSenders(
    DbContext dbContext, IStoreContext storeContext, ISecretStore secrets, EmailOptions options, ILogger<StoreSenders> logger) : IMailSenders
{
    public async Task<MailSender> CurrentAsync(CancellationToken cancellationToken)
    {
        var platform = new MailSender(options.Mailgun.Domain, options.Mailgun.ApiKey, options.SenderAddress, ReplyTo: null);

        if (storeContext.StoreId is null)
        {
            return platform;
        }

        var own = await dbContext.Set<SendingDomain>()
            .AsNoTracking()
            .SingleOrDefaultAsync(domain => domain.State == SendingDomainState.Active, cancellationToken);

        if (own is null)
        {
            return platform;
        }

        if (own.KeySecretName is null || await secrets.FindAsync(own.KeySecretName, cancellationToken) is not { Length: > 0 } key)
        {
            logger.LogWarning(
                "Store {StoreId} sends from {Domain}, but its sending key is not in the vault; this message goes out through the platform's domain.",
                own.StoreId,
                own.Domain);

            return platform;
        }

        return new MailSender(own.Domain, key, own.SenderAddress, own.ReplyTo);
    }
}
