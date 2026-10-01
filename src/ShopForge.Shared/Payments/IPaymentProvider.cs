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

// Everything a gateway may ask about an order. A provider takes what it needs and ignores the rest: the manual
// methods read almost none of it, Stripe reads the amount and the addresses it sends people to, and a gateway
// that wants to know who is paying and how the parcel reaches them can be told the truth rather than a guess.
//
// Facts about the order only. What the shop sells, how its gateway categorises goods and what a label may look
// like are the provider's own vocabulary and belong in its adapter.
public sealed record PaymentRequest(
    Guid StoreId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string ReturnUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt,
    string CustomerName = "",
    string CountryCode = "",
    string Language = "",
    PaymentDelivery Delivery = PaymentDelivery.ToAddress);

// How the goods reach the shopper, which some gateways ask for and which is a fact about the order rather than
// about the shipping method: a locker is a locker because this order is going to one.
public enum PaymentDelivery
{
    ToAddress,
    ToPickupPoint,
}

// Reference is what the provider calls this attempt — a session, a transaction — so that what it says later can
// be matched to what we sent. A method the store settles itself has none (D-141).
public sealed record PaymentInstructions(string Message, string? RedirectUrl = null, string? Reference = null);
