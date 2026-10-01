namespace ShopForge.Shared.Payments;

// Payment methods name the provider that handles them: "manual" for methods a store settles itself, "stripe" for
// hosted checkout. Orders never sees a provider's own types.
public interface IPaymentProvider
{
    string Key { get; }

    // Whether this provider takes money through a merchant account the store itself is connected to. The manual
    // methods do not, and the platform's own Stripe account is the deployment's (D-059); a gateway a merchant
    // signed up for does, and its methods are not offered by a store that has not connected one (D-138).
    bool NeedsConnection => false;

    Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken);
}

public sealed record PaymentRequest(
    Guid StoreId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string ReturnUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt);

// Reference is what the provider calls this attempt — a session, a transaction — so that what it says later can
// be matched to what we sent. A method the store settles itself has none (D-141).
public sealed record PaymentInstructions(string Message, string? RedirectUrl = null, string? Reference = null);
