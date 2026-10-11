using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Publishing;

// Whether a shop can take an order, which is two questions and used to be asked as one. A method switched on is
// not a method a shopper will be offered: a provider that needs a merchant account is left out of the checkout
// when it has none, deliberately and silently, so that nobody meets a payment page that cannot charge them. The
// consequence was a shop that published with card payments ticked and offered the shopper nothing (D-190).
//
// And a connection is not a credential. The gate on a connection is that a secret *name* is recorded; the secret
// itself lives in a vault and can be deleted, or the database can move to a deployment that reads credentials
// from configuration instead. Either way the name is still there and the money still cannot move, so this asks
// the secret store whether the value is actually readable.
internal sealed class OrdersPublishCheck(
    DbContext dbContext,
    IProviderConnections connections,
    ISecretStore secrets,
    IEnumerable<IPaymentProvider> paymentProviders,
    IEnumerable<IShippingProvider> shippingProviders) : IStorePublishCheck
{
    public async Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<PaymentMethod>()
            .AsNoTracking()
            .Where(method => method.IsActive)
            .Select(method => new { method.Name, method.ProviderKey })
            .ToListAsync(cancellationToken);

        var shipping = await dbContext.Set<ShippingMethod>()
            .AsNoTracking()
            .Where(method => method.IsActive)
            .Select(method => new { method.Name, method.ProviderKey })
            .ToListAsync(cancellationToken);

        var needsConnection = paymentProviders
            .Where(provider => provider.NeedsConnection)
            .Select(provider => provider.Key)
            .Concat(shippingProviders.Where(provider => provider.NeedsConnection).Select(provider => provider.Key))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            payment.Count > 0
                ? StoreReadiness.Ready("orders.payment-method")
                : StoreReadiness.NotReady("orders.payment-method", "The store has no active payment method."),
            shipping.Count > 0
                ? StoreReadiness.Ready("orders.shipping-method")
                : StoreReadiness.NotReady("orders.shipping-method", "The store has no active shipping method."),
            await ConnectedAsync("orders.payment-connected", "payment", [.. payment.Select(method => (method.Name, method.ProviderKey))], needsConnection, cancellationToken),
            await ConnectedAsync("orders.shipping-connected", "delivery", [.. shipping.Select(method => (method.Name, method.ProviderKey))], needsConnection, cancellationToken),
        ];
    }

    private async Task<StoreReadiness> ConnectedAsync(
        string check,
        string kind,
        IReadOnlyList<(string Name, string ProviderKey)> methods,
        HashSet<string> needsConnection,
        CancellationToken cancellationToken)
    {
        var unusable = new List<string>();

        foreach (var method in methods.Where(method => needsConnection.Contains(method.ProviderKey)))
        {
            if (await IsUsableAsync(method.ProviderKey, cancellationToken))
            {
                continue;
            }

            unusable.Add($"\"{method.Name}\" ({method.ProviderKey})");
        }

        return unusable.Count == 0
            ? StoreReadiness.Ready(check)
            : StoreReadiness.NotReady(
                check,
                $"{string.Join(", ", unusable)} {(unusable.Count == 1 ? "is" : "are")} switched on but not connected to an account, so {(unusable.Count == 1 ? "it" : "they")} would not be offered at the checkout. Connect {(unusable.Count == 1 ? "it" : "them")} or switch {(unusable.Count == 1 ? "it" : "them")} off.");
    }

    private async Task<bool> IsUsableAsync(string provider, CancellationToken cancellationToken) =>
        await connections.FindAsync(provider, cancellationToken) is { IsUsable: true } connection
        && await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 };
}
