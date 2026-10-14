namespace ShopForge.Infrastructure.Payments;

internal sealed class StripeOptions
{
    public const string Section = "Payments:Stripe";

    public string SecretKey { get; init; } = string.Empty;

    public string WebhookSecret { get; init; } = string.Empty;

    // Stripe's keys are the deployment's, so a payment through them lands in whoever runs it. That is right only
    // where every store on the deployment is that person's own, which the deployment has to say (D-196).
    public bool OfferToStores { get; init; }

    public bool IsConfigured => SecretKey.Length > 0 && WebhookSecret.Length > 0;
}
