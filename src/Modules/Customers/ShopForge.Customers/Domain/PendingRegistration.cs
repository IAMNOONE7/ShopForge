using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// What the customer typed when registering, kept until they prove the e-mail address: an unverified sign-up must not
// show up as a customer of the store.
internal sealed class PendingRegistration : IStoreOwned
{
    private PendingRegistration()
    {
    }

    public PendingRegistration(Guid customerIdentityId, string firstName, string lastName, string? phone, Guid storeId)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        CustomerIdentityId = customerIdentityId;
        FirstName = firstName;
        LastName = lastName;
        Phone = phone;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CustomerIdentityId { get; private set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }
}
