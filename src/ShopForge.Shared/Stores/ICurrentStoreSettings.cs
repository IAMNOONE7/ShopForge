namespace ShopForge.Shared.Stores;

public interface ICurrentStoreSettings
{
    Task<StoreSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record StoreSettings(
    string Name,
    string Currency,
    string Culture,
    int ReturnWindowDays,
    SellerDetails? Seller,
    StoreBranding Branding);

// How the store looks to somebody who is not on its website: its colour and its logo, with an address the logo can
// actually be fetched from, because a relative path means nothing in an inbox (D-121).
public sealed record StoreBranding(string PrimaryColor, string? LogoUrl);

// The store as it appears on a document: who is selling, and under which registration (D-078).
public sealed record SellerDetails(
    string LegalName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string RegistrationNumber,
    string? VatNumber);
