namespace ShopForge.Stores.Domain;

// Who the shop is on paper: the seller an invoice names (D-078).
internal sealed record StoreCompany(
    string LegalName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string RegistrationNumber,
    string? VatNumber)
{
    public static StoreCompany? From(
        string? legalName,
        string? line1,
        string? city,
        string? postalCode,
        string? country,
        string? registrationNumber,
        string? vatNumber)
    {
        if (string.IsNullOrWhiteSpace(legalName)
            || string.IsNullOrWhiteSpace(line1)
            || string.IsNullOrWhiteSpace(city)
            || string.IsNullOrWhiteSpace(postalCode)
            || country?.Trim().Length != 2
            || string.IsNullOrWhiteSpace(registrationNumber))
        {
            return null;
        }

        return new StoreCompany(
            legalName.Trim(),
            line1.Trim(),
            city.Trim(),
            postalCode.Trim(),
            country.Trim().ToUpperInvariant(),
            registrationNumber.Trim(),
            string.IsNullOrWhiteSpace(vatNumber) ? null : vatNumber.Trim());
    }
}
