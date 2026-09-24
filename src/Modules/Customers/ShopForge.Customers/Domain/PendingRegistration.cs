using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// What the customer typed when registering, kept until they prove the e-mail address: an unverified sign-up must not
// show up as a customer of the store.
internal sealed class PendingRegistration : IStoreOwned
{
    private PendingRegistration()
    {
    }

    public PendingRegistration(
        Guid customerIdentityId,
        string firstName,
        string lastName,
        string? phone,
        string passwordHash,
        Guid storeId,
        DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        CustomerIdentityId = customerIdentityId;
        CreatedAt = createdAt;
        Replace(firstName, lastName, phone, passwordHash, createdAt);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CustomerIdentityId { get; private set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }

    // Kept here rather than on the identity so that registering at one store cannot touch the password the customer
    // uses at another; it only ever becomes a credential when the address is proved (D-102).
    public string PasswordHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    // Registering again before proving the address replaces what is waiting, the way the newest link replaces the
    // previous one.
    public void Replace(string firstName, string lastName, string? phone, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        FirstName = firstName;
        LastName = lastName;
        Phone = phone;
        PasswordHash = passwordHash;
        CreatedAt = now;
    }
}
