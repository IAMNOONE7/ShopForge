namespace ShopForge.Shared.Documents;

// What a module hands over to get a PDF back; no business module knows how one is made (D-082).
public interface IDocumentRenderer
{
    byte[] Render(TaxDocument document);
}

public sealed record TaxDocument(
    string Title,
    string Number,
    string OrderNumber,
    DateTimeOffset IssuedAt,
    string Currency,
    string CultureName,
    DocumentParty Seller,
    DocumentParty Buyer,
    string PaymentMethod,
    IReadOnlyList<DocumentLine> Lines,
    IReadOnlyList<DocumentVatRate> VatSummary,
    decimal Net,
    decimal Vat,
    decimal Total);

public sealed record DocumentParty(string Name, IReadOnlyList<string> AddressLines, string? RegistrationNumber, string? VatNumber);

public sealed record DocumentLine(string Description, int Quantity, decimal UnitPrice, decimal VatRate, decimal LineTotal);

public sealed record DocumentVatRate(decimal Rate, decimal Net, decimal Vat, decimal Gross);
