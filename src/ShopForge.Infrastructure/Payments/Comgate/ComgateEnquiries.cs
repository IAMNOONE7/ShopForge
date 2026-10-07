using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Asking Comgate about an attempt nobody reported on. It is the same question the push path asks and the same
// answer, so the reconciliation sweep needs nothing of its own (D-176).
internal sealed class ComgateEnquiries(ComgateTransactions transactions) : IPaymentEnquiries
{
    public string Key => ComgatePaymentProvider.ProviderKey;

    public Task<PaymentNotification?> AskAsync(PaymentEnquiry enquiry, CancellationToken cancellationToken) =>
        transactions.ReadAsync(enquiry, cancellationToken);
}
