namespace ShopForge.Shared.Payments;

// Payment methods name the provider that handles them; Stage 6 ships "manual", later stages add hosted providers.
public interface IPaymentProvider
{
    string Key { get; }

    Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken);
}

public sealed record PaymentRequest(string OrderNumber, decimal Amount, string Currency, string CustomerEmail);

public sealed record PaymentInstructions(string Message, string? RedirectUrl = null);
