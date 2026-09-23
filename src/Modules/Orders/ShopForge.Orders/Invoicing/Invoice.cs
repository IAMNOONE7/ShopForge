using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// An accounting document, not a view of an order: everything it says is copied here when it is issued (D-080).
internal sealed class Invoice : IStoreOwned
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
    }

    public Invoice(
        Guid storeId,
        string number,
        InvoiceKind kind,
        string orderNumber,
        string currency,
        Seller seller,
        Address buyer,
        string buyerEmail,
        string paymentMethodName,
        DateTimeOffset issuedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Number = number;
        Kind = kind;
        OrderNumber = orderNumber;
        Currency = currency;
        Seller = seller;
        Buyer = buyer;
        BuyerEmail = buyerEmail;
        PaymentMethodName = paymentMethodName;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Number { get; private set; } = null!;

    public InvoiceKind Kind { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public Seller Seller { get; private set; } = null!;

    public Address Buyer { get; private set; } = null!;

    public string BuyerEmail { get; private set; } = null!;

    public string PaymentMethodName { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public decimal Total => _lines.Sum(line => line.LineTotal);

    public decimal VatTotal => _lines.Sum(line => Money.VatOf(line.LineTotal, line.VatRate));

    public decimal NetTotal => Total - VatTotal;

    public void AddLine(string description, int quantity, decimal unitPrice, decimal vatRate) =>
        _lines.Add(new InvoiceLine(description, quantity, unitPrice, vatRate));

    // What a VAT return needs: one row per rate, rather than per product (D-079).
    public IReadOnlyList<VatRateTotal> VatSummary() =>
    [
        .. _lines
            .GroupBy(line => line.VatRate)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var gross = group.Sum(line => line.LineTotal);
                var vat = Money.VatOf(gross, group.Key);

                return new VatRateTotal(group.Key, gross - vat, vat, gross);
            }),
    ];
}

internal sealed class InvoiceLine
{
    private InvoiceLine()
    {
    }

    internal InvoiceLine(string description, int quantity, decimal unitPrice, decimal vatRate)
    {
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        VatRate = vatRate;
    }

    public string Description { get; private set; } = null!;

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal VatRate { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;
}

internal sealed record Seller(
    string LegalName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string RegistrationNumber,
    string? VatNumber);

internal sealed record VatRateTotal(decimal Rate, decimal Net, decimal Vat, decimal Gross);

internal enum InvoiceKind
{
    Invoice,
    CreditNote,
}
