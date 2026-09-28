using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Privacy;

namespace ShopForge.Customers.Privacy;

// The account itself: who the store knows, and the list they kept. Erasing takes the relationship with it, and the
// identity behind it too once no other store of the company is still using that address (D-115, D-117).
internal sealed class CustomerAccountData(DbContext dbContext) : ICustomerData
{
    public async Task<IReadOnlyList<CustomerDataSection>> ExportAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Set<StoreCustomer>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == storeCustomerId, cancellationToken);

        if (customer is null)
        {
            return [];
        }

        var email = await dbContext.Set<CustomerIdentity>()
            .AsNoTracking()
            .Where(identity => identity.Id == customer.CustomerIdentityId)
            .Select(identity => identity.Email)
            .SingleAsync(cancellationToken);

        var wishlist = await dbContext.Set<WishlistItem>()
            .AsNoTracking()
            .Where(item => item.StoreCustomerId == storeCustomerId)
            .OrderBy(item => item.AddedAt)
            .Select(item => new WishlistExport(item.StoreProductId, item.AddedAt))
            .ToListAsync(cancellationToken);

        var consents = await dbContext.Set<CustomerConsent>()
            .AsNoTracking()
            .Where(consent => consent.StoreCustomerId == storeCustomerId)
            .Select(consent => new ConsentExport(consent.Purpose.ToString(), consent.IsGranted, consent.Statement, consent.DecidedAt, consent.IpAddress))
            .ToListAsync(cancellationToken);

        return
        [
            new CustomerDataSection(
                "account",
                [new AccountExport(email, customer.FirstName, customer.LastName, customer.Phone, customer.IsEmailVerified)]),
            new CustomerDataSection("wishlist", [.. wishlist]),
            new CustomerDataSection("consents", [.. consents]),
        ];
    }

    public async Task EraseAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Set<StoreCustomer>()
            .SingleOrDefaultAsync(candidate => candidate.Id == storeCustomerId, cancellationToken);

        if (customer is null)
        {
            return;
        }

        var identityId = customer.CustomerIdentityId;

        // Each of these would also go with the customer's row, which the database cascades. Erasure says what it
        // erases anyway: this is the list somebody checks against the law, not a consequence of a foreign key
        // declared in another file (D-117).
        dbContext.RemoveRange(await dbContext.Set<WishlistItem>()
            .Where(item => item.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.Set<EmailChange>()
            .Where(change => change.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.Set<CustomerConsent>()
            .Where(consent => consent.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken));

        // Links this store sent them, and a sign-up they never finished.
        dbContext.RemoveRange(await dbContext.Set<CustomerToken>()
            .Where(token => token.CustomerIdentityId == identityId)
            .ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.Set<PendingRegistration>()
            .Where(registration => registration.CustomerIdentityId == identityId)
            .ToListAsync(cancellationToken));

        dbContext.Remove(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The address itself lives on the identity, which the company's other stores may share (D-102). It goes
        // only once this was the last relationship using it — erasing here must not touch an account over there.
        var usedElsewhere = await dbContext.Set<StoreCustomer>()
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.CustomerIdentityId == identityId, cancellationToken);

        if (!usedElsewhere)
        {
            dbContext.Remove(await dbContext.Set<CustomerIdentity>().SingleAsync(identity => identity.Id == identityId, cancellationToken));
        }
    }

    private sealed record AccountExport(string Email, string FirstName, string LastName, string? Phone, bool IsEmailVerified);

    private sealed record WishlistExport(Guid StoreProductId, DateTimeOffset AddedAt);

    private sealed record ConsentExport(string Purpose, bool IsGranted, string Statement, DateTimeOffset DecidedAt, string? IpAddress);
}
