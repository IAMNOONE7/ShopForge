namespace ShopForge.Shared.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);

    // Mail that is nobody's shop's: an invitation to work on a company, a reset for somebody who runs the platform
    // (D-111).
    Task SendOutsideStoreAsync(EmailMessage message, CancellationToken cancellationToken);
}

// The text is what is written; the HTML is the same thing in the store's livery, added on the way out (D-121).
// A client that cannot show the HTML still has something to read.
public sealed record EmailMessage(string To, string Subject, string Body, string? HtmlBody = null);
