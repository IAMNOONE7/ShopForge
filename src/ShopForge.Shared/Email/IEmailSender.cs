namespace ShopForge.Shared.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);

    // Mail that is nobody's shop's: an invitation to work on a company, a reset for somebody who runs the platform
    // (D-111).
    Task SendOutsideStoreAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string Body);
