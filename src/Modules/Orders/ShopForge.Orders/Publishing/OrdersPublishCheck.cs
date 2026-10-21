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
            .Select(method => new { method.Name, method.ProviderKey, method.RequiresPickupPoint })
            .ToListAsync(cancellationToken);

        var needsConnection = paymentProviders
            .Where(provider => provider.NeedsConnection)
            .Select(provider => provider.Key)
            .Concat(shippingProviders.Where(provider => provider.NeedsConnection).Select(provider => provider.Key))
            .ToHashSet(StringComparer.Ordinal);

        // A method whose provider this deployment does not offer a store — one it has no keys for, or the platform's
        // own account (D-196) — is left out of the checkout as surely as one nobody connected.
        var offered = paymentProviders
            .Where(provider => provider.IsOfferedToStores)
            .Select(provider => provider.Key)
            .Concat(shippingProviders.Where(provider => provider.IsOfferedToStores).Select(provider => provider.Key))
            .ToHashSet(StringComparer.Ordinal);

        // A point chosen in the carrier's own map needs the key the map opens with, and the carrier is asked about the
        // point with that key too (D-213). Without it the method is offered and every order through it is refused.
        var mapped = shippingProviders
            .Where(provider => provider.PickupPoints == PickupPointChoice.InTheCarriersMap)
            .Select(provider => provider.Key)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            payment.Count > 0
                ? StoreReadiness.Ready("orders.payment-method")
                : StoreReadiness.NotReady("orders.payment-method", "The store has no active payment method."),
            shipping.Count > 0
                ? StoreReadiness.Ready("orders.shipping-method")
                : StoreReadiness.NotReady("orders.shipping-method", "The store has no active shipping method."),
            await ConnectedAsync("orders.payment-connected", [.. payment.Select(method => (method.Name, method.ProviderKey))], needsConnection, offered, [], cancellationToken),
            await ConnectedAsync(
                "orders.shipping-connected",
                [.. shipping.Select(method => (method.Name, method.ProviderKey))],
                needsConnection,
                offered,
                [.. shipping.Where(method => method.RequiresPickupPoint && mapped.Contains(method.ProviderKey)).Select(method => (method.Name, method.ProviderKey))],
                cancellationToken),
        ];
    }

    private async Task<StoreReadiness> ConnectedAsync(
        string check,
        IReadOnlyList<(string Name, string ProviderKey)> methods,
        HashSet<string> needsConnection,
        HashSet<string> offered,
        IReadOnlyList<(string Name, string ProviderKey)> chosenInAMap,
        CancellationToken cancellationToken)
    {
        var unoffered = methods.Where(method => !offered.Contains(method.ProviderKey)).Select(Named).ToList();
        var unusable = new List<string>();

        foreach (var method in methods.Where(method => needsConnection.Contains(method.ProviderKey)))
        {
            if (await IsUsableAsync(method.ProviderKey, cancellationToken))
            {
                continue;
            }

            unusable.Add(Named(method));
        }

        // Only where there is a connection at all: one with none is already named above.
        var keyless = new List<string>();

        foreach (var method in chosenInAMap)
        {
            if (await connections.FindAsync(method.ProviderKey, cancellationToken) is { IsUsable: true, PublishableKey: null or "" })
            {
                keyless.Add(Named(method));
            }
        }

        var problems = new List<string>();

        if (unusable.Count > 0)
        {
            problems.Add($"{string.Join(", ", unusable)} {(unusable.Count == 1 ? "is" : "are")} switched on but not connected to an account, so {(unusable.Count == 1 ? "it" : "they")} would not be offered at the checkout. Connect {(unusable.Count == 1 ? "it" : "them")} or switch {(unusable.Count == 1 ? "it" : "them")} off.");
        }

        if (keyless.Count > 0)
        {
            problems.Add($"{string.Join(", ", keyless)} {(keyless.Count == 1 ? "has its" : "have their")} pickup point chosen in the carrier's map, which needs the map key on the connection. Add the key, or switch {(keyless.Count == 1 ? "it" : "them")} off.");
        }

        if (unoffered.Count > 0)
        {
            problems.Add($"{string.Join(", ", unoffered)} {(unoffered.Count == 1 ? "is" : "are")} switched on, but this platform does not offer {(unoffered.Count == 1 ? "that provider" : "those providers")} to its stores, so {(unoffered.Count == 1 ? "it" : "they")} would not be offered at the checkout. Switch {(unoffered.Count == 1 ? "it" : "them")} off.");
        }

        return problems.Count == 0 ? StoreReadiness.Ready(check) : StoreReadiness.NotReady(check, string.Join(" ", problems));

        static string Named((string Name, string ProviderKey) method) => $"\"{method.Name}\" ({method.ProviderKey})";
    }

    private async Task<bool> IsUsableAsync(string provider, CancellationToken cancellationToken) =>
        await connections.FindAsync(provider, cancellationToken) is { IsUsable: true } connection
        && await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 };
}
