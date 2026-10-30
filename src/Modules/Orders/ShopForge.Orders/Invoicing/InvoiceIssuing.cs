using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Messaging;

namespace ShopForge.Orders.Invoicing;

// An order becomes a business record when it is paid, whoever confirmed the payment (D-079).
internal sealed class InvoiceIssuing(DbContext dbContext, Invoices invoices) : IEventHandler<PaymentReceived>
{
    public async Task HandleAsync(PaymentReceived domainEvent, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == domainEvent.OrderNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Order {domainEvent.OrderNumber} was paid but no longer exists.");

        // The order's row first, as receiving a return takes it, so this and a refund issuing the invoice for its credit
        // note never both find none: whichever comes second finds the first's (D-222).
        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM orders.orders WHERE id = {0} FOR UPDATE", [order.Id], cancellationToken);
        await invoices.IssueInvoiceAsync(order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
