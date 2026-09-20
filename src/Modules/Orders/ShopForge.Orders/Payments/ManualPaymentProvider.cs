using ShopForge.Shared.Payments;

namespace ShopForge.Orders.Payments;

// Methods a store settles outside the application: the customer gets instructions, nothing is charged online.
internal sealed class ManualPaymentProvider : IPaymentProvider
{
    public const string ProviderKey = "manual";

    public string Key => ProviderKey;

    public Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentInstructions(
            $"Your order {request.OrderNumber} is confirmed. Please pay {request.Amount:0.00} {request.Currency} as agreed with the store; " +
            "we will send the details to your e-mail."));
}
