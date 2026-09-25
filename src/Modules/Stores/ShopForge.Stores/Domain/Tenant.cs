namespace ShopForge.Stores.Domain;

// A company on the platform. Suspending one closes its shops and its admin without touching a row of its data, so
// it can be resumed the moment whatever caused it is settled (D-104).
internal sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        Status = TenantStatus.Active;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public TenantStatus Status { get; private set; }

    // Null while the company has not been put on a plan of its own: the default plan applies (D-108).
    public Guid? PlanId { get; private set; }

    public void MoveTo(Plan plan) => PlanId = plan.Id;

    public bool Suspend()
    {
        if (Status == TenantStatus.Suspended)
        {
            return false;
        }

        Status = TenantStatus.Suspended;

        return true;
    }

    public bool Resume()
    {
        if (Status == TenantStatus.Active)
        {
            return false;
        }

        Status = TenantStatus.Active;

        return true;
    }
}

internal enum TenantStatus
{
    Active,
    Suspended,
}
