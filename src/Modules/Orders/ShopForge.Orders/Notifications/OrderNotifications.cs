using ShopForge.Shared.Email;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Notifications;

// What a shop writes to a customer as an order moves along. The handlers run from the outbox, so a failed send is
// retried instead of being lost with the request that caused it (D-067).
internal sealed class OrderNotifications(IEmailSender email, ICurrentStoreSettings storeSettings)
    : IEventHandler<OrderPlaced>, IEventHandler<PaymentReceived>, IEventHandler<OrderCancelled>, IEventHandler<ShipmentCreated>
{
    public async Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Your order {domainEvent.OrderNumber}",
            $"Thank you for your order {domainEvent.OrderNumber} for {Amount(domainEvent.GrandTotal, domainEvent.Currency)}. {domainEvent.PaymentInstructions}",
            cancellationToken);

    public async Task HandleAsync(PaymentReceived domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Payment received for order {domainEvent.OrderNumber}",
            $"We received {Amount(domainEvent.GrandTotal, domainEvent.Currency)} for order {domainEvent.OrderNumber}. It is now being prepared.",
            cancellationToken);

    public async Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Order {domainEvent.OrderNumber} was cancelled",
            $"{domainEvent.Reason} Nothing has been charged, and the items are back on sale.",
            cancellationToken);

    public async Task HandleAsync(ShipmentCreated domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Order {domainEvent.OrderNumber} is on its way",
            $"Your order {domainEvent.OrderNumber} was handed to {domainEvent.Carrier} with tracking number {domainEvent.TrackingNumber}."
                + (domainEvent.PickupPoint is null ? string.Empty : $" You can collect it at {domainEvent.PickupPoint}."),
            cancellationToken);

    private static string Amount(decimal total, string currency) => $"{total:0.00} {currency}";

    private async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var store = (await storeSettings.GetAsync(cancellationToken)).Name;

        await email.SendAsync(new EmailMessage(recipient, $"{store}: {subject}", body), cancellationToken);
    }
}
