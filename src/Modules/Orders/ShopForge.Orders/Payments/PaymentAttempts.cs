using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Payments;

// The one lookup in Orders that reads past the store in scope, because a provider's callback has not told us
// which store it is yet. It is not a free search: the caller has already settled which stores the merchant sells
// under, and the reference only has to tell them apart. Nothing else is read here — the caller gets the store,
// the order and what was sent, and everything it does afterwards runs inside that store's scope (D-141).
internal sealed class PaymentAttempts(DbContext dbContext) : IPaymentAttempts
{
    public async Task<RecordedAttempt?> FindAsync(
        string provider,
        string reference,
        IReadOnlyCollection<Guid> stores,
        CancellationToken cancellationToken) =>
        stores.Count == 0
            ? null
            : await dbContext.Set<PaymentAttempt>()
                .IgnoreQueryFilters([TenancyFilters.Store])
                .AsNoTracking()
                .Where(attempt => attempt.Provider == provider
                    && attempt.Reference == reference
                    && stores.Contains(attempt.StoreId))
                .Select(attempt => new RecordedAttempt(attempt.StoreId, attempt.OrderNumber, attempt.Amount, attempt.Currency))
                .SingleOrDefaultAsync(cancellationToken);
}
