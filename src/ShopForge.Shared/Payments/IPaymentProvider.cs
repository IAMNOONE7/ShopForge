namespace ShopForge.Shared.Payments;

// Payment methods name the provider that handles them: "manual" for methods a store settles itself, "stripe" for
// hosted checkout. Orders never sees a provider's own types.
public interface IPaymentProvider
{
    string Key { get; }

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

public sealed record PaymentInstructions(string Message, string? RedirectUrl = null);
