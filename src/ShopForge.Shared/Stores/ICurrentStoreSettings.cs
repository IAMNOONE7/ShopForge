namespace ShopForge.Shared.Stores;

public interface ICurrentStoreSettings
{
    Task<StoreSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record StoreSettings(string Name, string Currency, string Culture, int ReturnWindowDays, SellerDetails? Seller);

// The store as it appears on a document: who is selling, and under which registration (D-078).
public sealed record SellerDetails(
    string LegalName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string RegistrationNumber,
    string? VatNumber);
