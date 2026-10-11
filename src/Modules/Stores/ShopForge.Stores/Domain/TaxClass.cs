using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// What a thing is, for tax: the standard rate, a reduced one, books, food — whatever the markets a shop sells
// into distinguish. The class says what the thing is; a rate says what a country charges for it (D-187).
//
// The code is an address and the name is a label (D-181): a listing names the code, so the code is fixed once
// it exists and only the label can be corrected.
internal sealed class TaxClass : IStoreOwned
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 100;

    private TaxClass()
    {
    }

    public TaxClass(Guid storeId, string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        Rename(name);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }
}
