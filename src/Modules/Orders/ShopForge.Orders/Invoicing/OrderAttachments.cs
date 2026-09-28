using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Returns;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Email;

namespace ShopForge.Orders.Invoicing;

// Turns the name a message carried into the document itself. Orders answers for these because Orders is what
// issues them; nothing else has to know that a receipt has an invoice behind it (D-122).
internal sealed class OrderAttachments(DbContext dbContext, Invoices invoices, IDocumentRenderer renderer) : IEmailAttachments
{
    public const string InvoiceForOrder = "order-invoice:";

    public const string CreditNoteForReturn = "return-credit-note:";

    public async Task<EmailAttachment?> FindAsync(string reference, CancellationToken cancellationToken)
    {
        var invoice = reference switch
        {
            _ when reference.StartsWith(InvoiceForOrder, StringComparison.Ordinal) =>
                await invoices.FindAsync(reference[InvoiceForOrder.Length..], InvoiceKind.Invoice, cancellationToken),
            _ when reference.StartsWith(CreditNoteForReturn, StringComparison.Ordinal) =>
                await CreditNoteAsync(reference[CreditNoteForReturn.Length..], cancellationToken),
            _ => null,
        };

        // The store filter is what keeps one shop's document out of another's mail: the message is delivered
        // inside the store it belongs to (D-111), and these queries see only that store.
        return invoice is null
            ? null
            : new EmailAttachment(
                $"{invoice.Number}.pdf",
                "application/pdf",
                renderer.Render(await invoices.ToDocumentAsync(invoice, cancellationToken)));
    }

    private async Task<Invoice?> CreditNoteAsync(string returnNumber, CancellationToken cancellationToken)
    {
        var orderReturn = await dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Number == returnNumber, cancellationToken);

        return orderReturn is null
            ? null
            : await dbContext.Set<Invoice>().SingleOrDefaultAsync(candidate => candidate.ReturnId == orderReturn.Id, cancellationToken);
    }
}
