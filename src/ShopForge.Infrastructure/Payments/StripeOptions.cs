namespace ShopForge.Infrastructure.Payments;

internal sealed class StripeOptions
{
    public const string Section = "Payments:Stripe";

    public string SecretKey { get; init; } = string.Empty;

    public string WebhookSecret { get; init; } = string.Empty;

    public bool IsConfigured => SecretKey.Length > 0 && WebhookSecret.Length > 0;
}
