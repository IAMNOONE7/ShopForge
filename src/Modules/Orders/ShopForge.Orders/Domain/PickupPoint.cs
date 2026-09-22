using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// Places a store hands parcels over itself: its shops, a counter, a locker it operates (D-063).
internal sealed class StorePickupPoint : IStoreOwned
{
    private StorePickupPoint()
    {
    }

    public StorePickupPoint(Guid storeId, string code, string name, Address address, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        Update(name, address, isActive);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public Address Address { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public void Update(string name, Address address, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Address = address;
        IsActive = isActive;
    }
}
