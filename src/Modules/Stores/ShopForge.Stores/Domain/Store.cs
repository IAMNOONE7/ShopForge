using System.Globalization;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

internal sealed class Store : ITenantOwned
{
    private readonly List<StoreDomain> _domains = [];

    private Store()
    {
    }

    public Store(Guid tenantId, string name, string currency, string culture, StoreTheme theme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Name = name.Trim();
        Currency = ToCurrencyCode(currency);
        Culture = CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name;
        Theme = theme;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public string Culture { get; private set; } = null!;

    public StoreTheme Theme { get; private set; } = null!;

    public IReadOnlyCollection<StoreDomain> Domains => _domains;

    public StoreDomain AddDomain(string hostName)
    {
        var domain = new StoreDomain(Id, hostName, isPrimary: _domains.Count == 0);

        if (_domains.Any(existing => existing.HostName == domain.HostName))
        {
            throw new InvalidOperationException($"Store already has the domain '{domain.HostName}'.");
        }

        _domains.Add(domain);
        return domain;
    }

    private static string ToCurrencyCode(string currency)
    {
        var code = currency.Trim().ToUpperInvariant();

        return code.Length == 3 && code.All(char.IsAsciiLetterUpper)
            ? code
            : throw new ArgumentException($"'{currency}' is not an ISO 4217 currency code.", nameof(currency));
    }
}
