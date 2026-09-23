using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Messaging;

namespace ShopForge.Orders.Invoicing;

// An order becomes a business record when it is paid, whoever confirmed the payment (D-079).
internal sealed class InvoiceIssuing(DbContext dbContext, Invoices invoices) : IEventHandler<PaymentReceived>
{
    public async Task HandleAsync(PaymentReceived domainEvent, CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == domainEvent.OrderNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Order {domainEvent.OrderNumber} was paid but no longer exists.");

        await invoices.IssueAsync(order, InvoiceKind.Invoice, cancellationToken);
    }
}
