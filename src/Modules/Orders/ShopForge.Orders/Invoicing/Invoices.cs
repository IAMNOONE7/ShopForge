using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Persistence;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Invoicing;

internal sealed class Invoices(DbContext dbContext, IStoreContext storeContext, ICurrentStoreSettings storeSettings, TimeProvider clock)
{
    public Task<Invoice?> FindAsync(string orderNumber, InvoiceKind kind, CancellationToken cancellationToken) =>
        dbContext.Set<Invoice>()
            .SingleOrDefaultAsync(invoice => invoice.OrderNumber == orderNumber && invoice.Kind == kind, cancellationToken);

    // Issuing twice for one order is what an at-least-once event would cause, so the existing document wins (D-079).
    public async Task<Invoice> IssueAsync(Order order, InvoiceKind kind, CancellationToken cancellationToken)
    {
        if (await FindAsync(order.Number, kind, cancellationToken) is { } existing)
        {
            return existing;
        }

        var settings = await storeSettings.GetAsync(cancellationToken);
        var seller = settings.Seller
            ?? throw new InvalidOperationException($"Store {storeContext.StoreId} has no company details, so no document can be issued.");

        var issuedAt = clock.GetUtcNow();
        var series = kind == InvoiceKind.CreditNote ? NumberSeries.CreditNote : NumberSeries.Invoice;
        var number = await Numbers.NextDocumentNumberAsync(dbContext, order.StoreId, series, issuedAt.Year, cancellationToken);

        var invoice = new Invoice(
            order.StoreId,
            number,
            kind,
            order.Number,
            order.Currency,
            new Seller(
                seller.LegalName,
                seller.Line1,
                seller.City,
                seller.PostalCode,
                seller.Country,
                seller.RegistrationNumber,
                seller.VatNumber),
            order.BillingAddress,
            order.Email,
            order.PaymentMethodName,
            order.DiscountCode,
            issuedAt);

        foreach (var line in order.Lines)
        {
            invoice.AddLine(line.ProductName, line.Quantity, line.UnitPrice, line.VatRate, line.Discount);
        }

        if (order.ShippingPrice > 0)
        {
            invoice.AddLine(order.ShippingMethodName, 1, order.ShippingPrice, order.ShippingVatRate, order.ShippingDiscount);
        }

        dbContext.Add(invoice);
        await dbContext.SaveChangesAsync(cancellationToken);

        return invoice;
    }

    public async Task<TaxDocument> ToDocumentAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var settings = await storeSettings.GetAsync(cancellationToken);
        var sign = invoice.Kind == InvoiceKind.CreditNote ? -1m : 1m;

        return new TaxDocument(
            invoice.Kind == InvoiceKind.CreditNote ? "Credit note" : "Invoice",
            invoice.Number,
            invoice.OrderNumber,
            invoice.IssuedAt,
            invoice.Currency,
            settings.Culture,
            new DocumentParty(
                invoice.Seller.LegalName,
                [invoice.Seller.Line1, $"{invoice.Seller.PostalCode} {invoice.Seller.City}", invoice.Seller.Country],
                invoice.Seller.RegistrationNumber,
                invoice.Seller.VatNumber),
            new DocumentParty(
                invoice.Buyer.FullName,
                [.. AddressLines(invoice.Buyer)],
                RegistrationNumber: null,
                VatNumber: null),
            invoice.PaymentMethodName,
            invoice.DiscountCode,
            [
                .. invoice.Lines.Select(line => new DocumentLine(
                    line.Description,
                    line.Quantity,
                    sign * line.UnitPrice,
                    line.VatRate,
                    sign * line.Discount,
                    sign * line.LineTotal)),
            ],
            [.. invoice.VatSummary().Select(rate => new DocumentVatRate(rate.Rate, sign * rate.Net, sign * rate.Vat, sign * rate.Gross))],
            sign * invoice.NetTotal,
            sign * invoice.VatTotal,
            sign * invoice.Total);
    }

    private static IEnumerable<string> AddressLines(Address address)
    {
        yield return address.Line1;

        if (address.Line2 is { Length: > 0 })
        {
            yield return address.Line2;
        }

        yield return $"{address.PostalCode} {address.City}";
        yield return address.Country;
    }
}
