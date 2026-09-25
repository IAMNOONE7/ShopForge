namespace ShopForge.Stores.Domain;

// What a company may have on the platform. A cap is a commercial limit rather than an invariant: it stops the next
// store or product being created and never touches what is already there (D-108).
internal sealed class Plan
{
    private Plan()
    {
    }

    public Plan(string code, string name, int? maxStores, int? maxProducts, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        Code = code.Trim().ToLowerInvariant();
        IsDefault = isDefault;
        Update(name, maxStores, maxProducts);
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    // Null is no cap at all, which is what the largest plan is for.
    public int? MaxStores { get; private set; }

    public int? MaxProducts { get; private set; }

    // The plan a tenant is on until it is put on another one, so every company always has caps (D-108).
    public bool IsDefault { get; private set; }

    public void Update(string name, int? maxStores, int? maxProducts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStores ?? 0, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxProducts ?? 0, 0);

        Name = name.Trim();
        MaxStores = maxStores;
        MaxProducts = maxProducts;
    }
}

internal static class PlanCodes
{
    public const string Starter = "starter";
    public const string Growth = "growth";
    public const string Scale = "scale";
}
