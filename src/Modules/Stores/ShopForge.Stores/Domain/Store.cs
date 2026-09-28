using System.Globalization;
using ShopForge.Shared.Files;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

internal sealed class Store : ITenantOwned
{
    // The statutory minimum in the EU; a store that takes goods back for longer says so in its settings (D-099).
    public const int DefaultReturnWindowDays = 14;

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
        Status = StoreStatus.Draft;
        ReturnWindowDays = DefaultReturnWindowDays;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public string Culture { get; private set; } = null!;

    public StoreTheme Theme { get; private set; } = null!;

    public string? LogoPath { get; private set; }

    public StoreStatus Status { get; private set; }

    // Null until the store fills it in; a store cannot be published without it, because an invoice names a seller.
    public StoreCompany? Company { get; private set; }

    // How long after a delivery the store takes goods back.
    public int ReturnWindowDays { get; private set; } = DefaultReturnWindowDays;

    public IReadOnlyCollection<StoreDomain> Domains => _domains;

    // The subdomain a store is given when it is created: the platform owns it, so there is nothing to prove.
    public StoreDomain AddDomain(string hostName, DateTimeOffset now)
    {
        var domain = StoreDomain.OwnedByThePlatform(Id, hostName, isPrimary: _domains.Count == 0, now);

        return Add(domain);
    }

    // A name the merchant says is theirs. It is served only once they have proved it (D-124).
    public StoreDomain ClaimDomain(string hostName) => Add(StoreDomain.ClaimedByTheStore(Id, hostName));

    public void MakePrimary(StoreDomain domain)
    {
        foreach (var other in _domains)
        {
            other.GiveUpPrimary();
        }

        domain.MakePrimary();
    }

    public void RemoveDomain(StoreDomain domain) => _domains.Remove(domain);

    private StoreDomain Add(StoreDomain domain)
    {
        if (_domains.Any(existing => existing.HostName == domain.HostName))
        {
            throw new InvalidOperationException($"Store already has the domain '{domain.HostName}'.");
        }

        _domains.Add(domain);

        return domain;
    }

    public void SetCompany(StoreCompany company) => Company = company;

    public void UpdateSettings(string name, string? currency, string culture, StoreTheme theme, int returnWindowDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(returnWindowDays);

        if (currency is not null)
        {
            if (Status != StoreStatus.Draft && ToCurrencyCode(currency) != Currency)
            {
                throw new InvalidOperationException("The currency of a published store cannot be changed.");
            }

            Currency = ToCurrencyCode(currency);
        }

        Name = name.Trim();
        Culture = CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name;
        Theme = theme;
        ReturnWindowDays = returnWindowDays;
    }

    public void Publish() => Status = StoreStatus.Published;

    public void Unpublish() => Status = StoreStatus.Draft;

    public string? ReplaceLogo(string contentType)
    {
        var previousPath = LogoPath;
        LogoPath = $"tenants/{TenantId}/stores/{Id}/logo-{Guid.CreateVersion7()}{ImageFormats.ExtensionFor(contentType)}";

        return previousPath;
    }

    private static string ToCurrencyCode(string currency)
    {
        var code = currency.Trim().ToUpperInvariant();

        return code.Length == 3 && code.All(char.IsAsciiLetterUpper)
            ? code
            : throw new ArgumentException($"'{currency}' is not an ISO 4217 currency code.", nameof(currency));
    }
}
