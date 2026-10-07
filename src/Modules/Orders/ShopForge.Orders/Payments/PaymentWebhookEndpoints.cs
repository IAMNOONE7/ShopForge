using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Payments;

// Payment results arrive here, not from the browser coming back from the provider (D-057). The request carries no
// store: the provider's payload names it, and everything after that runs inside that store's scope.
internal static class PaymentWebhookEndpoints
{
    public static void MapPaymentWebhooks(this IEndpointRouteBuilder payments)
    {
        payments.MapPost("/{provider}", HandleAsync);
    }

    private static async Task<Results<Ok, BadRequest, NotFound>> HandleAsync(
        string provider,
        HttpContext httpContext,
        StoreContext storeContext,
        IStoreDirectory stores,
        PaymentResults results,
        IEnumerable<IPaymentNotifications> notifications,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(PaymentWebhookEndpoints));
        var reader = notifications.SingleOrDefault(candidate => candidate.Key == provider);

        if (reader is null)
        {
            return TypedResults.NotFound();
        }

        var notification = await reader.ReadAsync(httpContext.Request, cancellationToken);

        if (notification is null)
        {
            return TypedResults.BadRequest();
        }

        var store = await stores.FindAsync(notification.StoreId, cancellationToken);

        if (store is null)
        {
            logger.LogWarning("A {Provider} event named store {StoreId}, which does not exist.", provider, notification.StoreId);

            return TypedResults.NotFound();
        }

        storeContext.Set(store.StoreId, store.TenantId);

        // Everything from here is shared with the reconciliation sweep, so a push and a sweep that arrive
        // together land one effect between them rather than one each (D-176).
        return await results.RecordAsync(provider, notification, cancellationToken) == PaymentRecord.NoSuchOrder
            ? TypedResults.NotFound()
            : TypedResults.Ok();
    }
}
