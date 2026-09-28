using System.Globalization;
using ShopForge.Orders.Invoicing;
using ShopForge.Shared.Email;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Notifications;

// What a shop writes to a customer as an order moves along. The handlers run from the outbox, so a failed send is
// retried instead of being lost with the request that caused it (D-067).
internal sealed class OrderNotifications(IEmailSender email, ICurrentStoreSettings storeSettings)
    : IEventHandler<OrderPlaced>,
        IEventHandler<PaymentReceived>,
        IEventHandler<OrderCancelled>,
        IEventHandler<ShipmentCreated>,
        IEventHandler<ReturnDecided>,
        IEventHandler<ReturnRefunded>
{
    public async Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Your order {domainEvent.OrderNumber}",
            store => $"Thank you for your order {domainEvent.OrderNumber} for {Amount(domainEvent.GrandTotal, domainEvent.Currency, store)}.\n{domainEvent.PaymentInstructions}",
            cancellationToken);

    public async Task HandleAsync(PaymentReceived domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Payment received for order {domainEvent.OrderNumber}",
            store => $"We received {Amount(domainEvent.GrandTotal, domainEvent.Currency, store)} for order {domainEvent.OrderNumber}. It is now being prepared."
                + "\nThe invoice is attached.",
            cancellationToken,
            OrderAttachments.InvoiceForOrder + domainEvent.OrderNumber);

    public async Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Order {domainEvent.OrderNumber} was cancelled",
            _ => $"{domainEvent.Reason} Nothing has been charged, and the items are back on sale.",
            cancellationToken);

    public async Task HandleAsync(ShipmentCreated domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Order {domainEvent.OrderNumber} is on its way",
            _ => $"Your order {domainEvent.OrderNumber} was handed to {domainEvent.Carrier} with tracking number {domainEvent.TrackingNumber}."
                + (domainEvent.PickupPoint is null ? string.Empty : $" You can collect it at {domainEvent.PickupPoint}."),
            cancellationToken);

    public async Task HandleAsync(ReturnDecided domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Return {domainEvent.ReturnNumber} for order {domainEvent.OrderNumber}",
            _ => domainEvent.Accepted
                ? $"Please send the items back to us. Once they arrive we refund them and you get a credit note for return {domainEvent.ReturnNumber}."
                : $"We cannot take these items back, so return {domainEvent.ReturnNumber} is closed. Write back to us if you think this is wrong.",
            cancellationToken);

    public async Task HandleAsync(ReturnRefunded domainEvent, CancellationToken cancellationToken) =>
        await SendAsync(
            domainEvent.Email,
            $"Refund for order {domainEvent.OrderNumber}",
            store => $"Your return {domainEvent.ReturnNumber} arrived and {Amount(domainEvent.Amount, domainEvent.Currency, store)} is on its way back to you."
                + "\nThe credit note is attached.",
            cancellationToken,
            OrderAttachments.CreditNoteForReturn + domainEvent.ReturnNumber);

    // Written the way the store's own customers read money, rather than the way the server happens to be set up.
    // A culture that does not know the currency still gets the amount and the code, which beats a wrong symbol.
    private static string Amount(decimal total, string currency, StoreSettings store)
    {
        var culture = CultureInfo.GetCultureInfo(store.Culture);
        var region = new RegionInfo(culture.Name);

        return string.Equals(region.ISOCurrencySymbol, currency, StringComparison.OrdinalIgnoreCase)
            ? total.ToString("C", culture)
            : $"{total.ToString("N2", culture)} {currency}";
    }

    private async Task SendAsync(
        string recipient,
        string subject,
        Func<StoreSettings, string> body,
        CancellationToken cancellationToken,
        string? attachmentReference = null)
    {
        var store = await storeSettings.GetAsync(cancellationToken);

        await email.SendAsync(
            new EmailMessage(recipient, $"{store.Name}: {subject}", body(store), AttachmentReference: attachmentReference),
            cancellationToken);
    }
}
