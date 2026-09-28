namespace ShopForge.Shared.Auditing;

// What was done, by whom, to what. Written with the change that caused it, so a decision that rolls back leaves no
// record of having happened, and read back by people answering a dispute or a support question.
//
// It is a record and nothing hangs off it: no handler runs, no e-mail is sent, nothing branches on an entry (D-116).
// Actions are named "<subject>.<what happened>" — "tenant.suspended", "order.refunded", "stock.set".
public interface IAuditLog
{
    // For work inside a company: the tenant and store in scope are the ones the entry belongs to.
    void Record(string action, string subject, object? details = null);

    // For the platform acting on a company it is not inside. Nothing else may reach across tenants this way.
    void RecordForTenant(Guid tenantId, string action, string subject, object? details = null);
}
