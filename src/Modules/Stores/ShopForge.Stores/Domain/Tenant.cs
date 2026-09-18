namespace ShopForge.Stores.Domain;

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
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;
}
