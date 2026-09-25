using ShopForge.Infrastructure.Messaging;
using ShopForge.Shared.Security;

namespace ShopForge.Api.Messaging;

// Messages that gave up are a store's business: it can see what failed and ask for it to be tried again (D-068).
internal static class AdminOutboxEndpoints
{
    public static IEndpointRouteBuilder MapAdminOutboxEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var messages = storeAdmin.MapGroup("/failed-messages");

        messages.MapGet("/", async (OutboxAdmin outbox, CancellationToken cancellationToken) =>
            TypedResults.Ok(await outbox.FailedAsync(cancellationToken)));

        messages.MapPost("/{messageId:guid}/requeue", async (Guid messageId, OutboxAdmin outbox, CancellationToken cancellationToken) =>
                await outbox.RequeueAsync(messageId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .RequireAuthorization(AdminPolicies.StoreManagement);

        return storeAdmin;
    }
}

// Messages belonging to no store have no shop to notice them, so the platform is where they surface (D-111).
internal static class PlatformOutboxEndpoints
{
    public static IEndpointRouteBuilder MapPlatformOutboxEndpoints(this IEndpointRouteBuilder platform)
    {
        var messages = platform.MapGroup("/failed-messages");

        messages.MapGet("/", async (OutboxAdmin outbox, CancellationToken cancellationToken) =>
            TypedResults.Ok(await outbox.FailedOutsideStoresAsync(cancellationToken)));

        messages.MapPost("/{messageId:guid}/requeue", async (Guid messageId, OutboxAdmin outbox, CancellationToken cancellationToken) =>
            await outbox.RequeueOutsideStoresAsync(messageId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        return platform;
    }
}
