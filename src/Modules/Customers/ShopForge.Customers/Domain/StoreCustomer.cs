using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

internal sealed class StoreCustomer : IStoreOwned
{
    public const int MaxNameLength = 100;

    private StoreCustomer()
    {
    }

    public StoreCustomer(Guid storeId, Guid customerIdentityId, string firstName, string lastName, string? phone, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        CustomerIdentityId = customerIdentityId;
        PasswordHash = passwordHash;
        SetDetails(firstName, lastName, phone);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CustomerIdentityId { get; private set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }

    // The password for this store and the proof of the address given to this store. The same person shopping at
    // another store of the company has their own, and neither store can see or change the other's (D-102).
    public string PasswordHash { get; private set; } = null!;

    public bool IsEmailVerified { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
    }

    public void VerifyEmail() => IsEmailVerified = true;

    public void SetDetails(string firstName, string lastName, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }
}
