using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Returns;
using ShopForge.Shared.Privacy;

namespace ShopForge.Orders.Privacy;

// Orders are the one place where erasing cannot mean deleting: the shop has to be able to show what it sold, for
// what, and with which VAT, years after the buyer is gone. So the numbers stay and the person goes (D-117).
internal sealed class CustomerOrderData(DbContext dbContext) : ICustomerData
{
    public async Task<IReadOnlyList<CustomerDataSection>> ExportAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Set<Order>()
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => order.StoreCustomerId == storeCustomerId)
            .OrderBy(order => order.PlacedAt)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return [];
        }

        var numbers = orders.Select(order => order.Number).ToList();
        var returns = await dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .Include(orderReturn => orderReturn.Lines)
            .Where(orderReturn => orderReturn.StoreCustomerId == storeCustomerId)
            .OrderBy(orderReturn => orderReturn.RequestedAt)
            .ToListAsync(cancellationToken);
        var invoices = await dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(invoice => numbers.Contains(invoice.OrderNumber))
            .OrderBy(invoice => invoice.IssuedAt)
            .Select(invoice => new InvoiceExport(invoice.Number, invoice.Kind.ToString(), invoice.OrderNumber, invoice.Currency, invoice.Total, invoice.IssuedAt))
            .ToListAsync(cancellationToken);

        return
        [
            new CustomerDataSection("orders", [.. orders.Select(Export)]),
            new CustomerDataSection("returns", [.. returns.Select(Export)]),
            new CustomerDataSection("invoices", [.. invoices]),
        ];
    }

    public async Task EraseAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Set<Order>()
            .Where(order => order.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken);

        // A return is the store's record of goods coming back, so it stays — but what the customer typed about
        // themselves does not, and neither does the link to them.
        foreach (var orderReturn in await dbContext.Set<OrderReturn>()
            .Where(orderReturn => orderReturn.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken))
        {
            orderReturn.Anonymise();
        }

        if (orders.Count == 0)
        {
            return;
        }

        var numbers = orders.Select(order => order.Number).ToList();

        foreach (var order in orders)
        {
            order.Anonymise();
        }

        foreach (var invoice in await dbContext.Set<Invoice>()
            .Where(invoice => numbers.Contains(invoice.OrderNumber))
            .ToListAsync(cancellationToken))
        {
            invoice.Anonymise();
        }
    }

    private static OrderExport Export(Order order) => new(
        order.Number,
        order.Status.ToString(),
        order.Currency,
        order.GrandTotal,
        order.PlacedAt,
        order.Email,
        Describe(order.BillingAddress),
        Describe(order.ShippingAddress),
        [.. order.Lines.Select(line => new OrderLineExport(line.ProductName, line.Quantity, line.UnitPrice, line.VatRate))]);

    private static ReturnExport Export(OrderReturn orderReturn) => new(
        orderReturn.Number,
        orderReturn.Status.ToString(),
        orderReturn.Reason,
        orderReturn.RequestedAt,
        orderReturn.RefundedAmount,
        [.. orderReturn.Lines.Select(line => new ReturnLineExport(line.ProductName, line.Quantity))]);

    private static AddressExport Describe(Address address) =>
        new(address.FullName, address.Line1, address.Line2, address.City, address.PostalCode, address.Country);

    private sealed record OrderExport(
        string Number,
        string Status,
        string Currency,
        decimal GrandTotal,
        DateTimeOffset PlacedAt,
        string Email,
        AddressExport BillingAddress,
        AddressExport ShippingAddress,
        IReadOnlyList<OrderLineExport> Lines);

    private sealed record OrderLineExport(string ProductName, int Quantity, decimal UnitPrice, decimal VatRate);

    private sealed record AddressExport(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country);

    private sealed record ReturnExport(
        string Number,
        string Status,
        string? Reason,
        DateTimeOffset RequestedAt,
        decimal RefundedAmount,
        IReadOnlyList<ReturnLineExport> Lines);

    private sealed record ReturnLineExport(string ProductName, int Quantity);

    private sealed record InvoiceExport(string Number, string Kind, string OrderNumber, string Currency, decimal Total, DateTimeOffset IssuedAt);
}
