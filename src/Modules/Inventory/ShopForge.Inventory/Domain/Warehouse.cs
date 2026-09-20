using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

internal sealed class Warehouse : ITenantOwned
{
    public const string DefaultCode = "main";

    private Warehouse()
    {
    }

    public Warehouse(Guid tenantId, string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Code = code.Trim().ToLowerInvariant();
        Name = name.Trim();
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;
}
