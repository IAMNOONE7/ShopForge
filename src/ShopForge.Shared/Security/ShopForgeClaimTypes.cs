namespace ShopForge.Shared.Security;

public static class ShopForgeClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string StoreId = "store_id";
    public const string StoreCustomerId = "store_customer_id";

    // Which generation of somebody's sessions a cookie belongs to; a cookie from an older one is no longer theirs.
    public const string SecurityStamp = "security_stamp";
}
