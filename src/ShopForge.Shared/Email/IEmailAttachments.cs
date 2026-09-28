namespace ShopForge.Shared.Email;

// How a named document becomes bytes, answered by whichever module owns it. The name travels in the message; the
// document is found or made at the moment the mail goes out, which is also the moment it is certain to exist —
// an invoice is issued by a handler of the very event that asks for the receipt (D-122).
//
// A module that does not recognise the name says nothing, and mail with a document nobody can find is still sent
// without it: a receipt that arrives bare beats a receipt that never arrives.
public interface IEmailAttachments
{
    Task<EmailAttachment?> FindAsync(string reference, CancellationToken cancellationToken);
}
