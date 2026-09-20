namespace ShopForge.Orders.Domain;

internal sealed record Address
{
    public Address(string fullName, string line1, string? line2, string city, string postalCode, string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(line1);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);

        if (country.Trim().Length != 2)
        {
            throw new ArgumentException("Country must be a two-letter code.", nameof(country));
        }

        FullName = fullName.Trim();
        Line1 = line1.Trim();
        Line2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        City = city.Trim();
        PostalCode = postalCode.Trim();
        Country = country.Trim().ToUpperInvariant();
    }

    public string FullName { get; private init; }

    public string Line1 { get; private init; }

    public string? Line2 { get; private init; }

    public string City { get; private init; }

    public string PostalCode { get; private init; }

    public string Country { get; private init; }
}
