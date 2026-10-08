namespace ShopForge.Shared.Tenancy;

public static class TenancyFilters
{
    public const string Store = "Store";
    public const string Tenant = "Tenant";

    // Rows a merchant has retired. Named, so the few places that deliberately want them — the admin list that
    // offers to restore one, the delete that checks what points at it — can ask for them (D-180).
    public const string Archived = "Archived";
}
