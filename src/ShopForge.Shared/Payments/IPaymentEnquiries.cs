namespace ShopForge.Shared.Payments;

// Providers that can be asked what became of a payment, for when nobody called. A notification can be lost —
// a deploy mid-flight, a network that swallowed it, a provider that gave up retrying — and a shopper who paid
// is owed their order either way (D-176).
//
// The answer comes back in the same shape a push produces, carrying the same event id, so that it takes the
// same path and lands the same effect. That is what makes a sweep and a late notification arriving together
// safe: they are not two routes to one outcome, they are one route.
public interface IPaymentEnquiries
{
    string Key { get; }

    // Null when the provider could not say: a transaction it does not know, a connection that cannot be read,
    // a call that failed. None of those is an answer, and an attempt is left alone to be asked again.
    Task<PaymentNotification?> AskAsync(PaymentEnquiry enquiry, CancellationToken cancellationToken);
}

// What was sent, so the provider's answer can be checked against it rather than believed.
public sealed record PaymentEnquiry(Guid StoreId, string OrderNumber, string Reference, decimal Amount, string Currency);
