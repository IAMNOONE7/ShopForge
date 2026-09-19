namespace ShopForge.Catalog.Domain;

internal sealed class ProductImage
{
    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    private ProductImage()
    {
    }

    internal ProductImage(Guid productId, Guid tenantId, string contentType, string? altText, int position)
    {
        if (!Extensions.TryGetValue(contentType, out var extension))
        {
            throw new ArgumentException($"'{contentType}' is not a supported image type.", nameof(contentType));
        }

        Id = Guid.CreateVersion7();
        ContentType = contentType;
        AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        Position = position;
        FilePath = $"tenants/{tenantId}/products/{productId}/{Id}{extension}";
    }

    public Guid Id { get; private set; }

    public string FilePath { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public string? AltText { get; private set; }

    public int Position { get; private set; }

    internal void MoveTo(int position) => Position = position;
}
