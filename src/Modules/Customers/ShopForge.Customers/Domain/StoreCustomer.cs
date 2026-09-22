using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

internal sealed class StoreCustomer : IStoreOwned
{
    public const int MaxNameLength = 100;

    private StoreCustomer()
    {
    }

    public StoreCustomer(Guid storeId, Guid customerIdentityId, string firstName, string lastName, string? phone)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        CustomerIdentityId = customerIdentityId;
        SetDetails(firstName, lastName, phone);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CustomerIdentityId { get; private set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public void SetDetails(string firstName, string lastName, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }
}
