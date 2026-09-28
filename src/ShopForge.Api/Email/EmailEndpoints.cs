using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Api.Email;

// What the provider tells us after the fact, and what a store can do about it. The webhook is open to the
// internet, so it believes nothing that is not signed (D-123).
internal static class EmailEndpoints
{
    public static IEndpointRouteBuilder MapEmailWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/email/mailgun", async (
            HttpContext httpContext,
            MailgunWebhook webhook,
            IServiceProvider services,
            IStoreDirectory stores,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(EmailEndpoints));

            if (!webhook.CanVerify)
            {
                logger.LogWarning("A Mailgun event arrived with no signing key configured; it was ignored.");

                return Results.Unauthorized();
            }

            using var reader = new StreamReader(httpContext.Request.Body);
            var delivery = webhook.Read(await reader.ReadToEndAsync(cancellationToken));

            if (delivery is null)
            {
                return Results.BadRequest();
            }

            // The suppression belongs to the store the message went out for, so it is written inside that store's
            // scope; one that names no store belongs to the platform's own mail (D-111).
            await using var scope = services.CreateAsyncScope();

            if (delivery.StoreId is { } storeId && await stores.FindAsync(storeId, cancellationToken) is { } store)
            {
                scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);
            }

            await scope.ServiceProvider.GetRequiredService<EmailSuppression>()
                .SuppressAsync(delivery.StoreId, delivery.Recipient, delivery.Reason, delivery.Detail, cancellationToken);

            logger.LogInformation("{Recipient} was suppressed: {Reason}.", delivery.Recipient, delivery.Reason);

            return Results.Ok();
        });

        return app;
    }

    public static IEndpointRouteBuilder MapAdminSuppressionEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var addresses = storeAdmin.MapGroup("/suppressed-addresses");

        addresses.MapGet("/", async (EmailSuppression suppression, CancellationToken cancellationToken) =>
            TypedResults.Ok(await suppression.ListAsync(cancellationToken)));

        addresses.MapDelete("/{id:guid}", async (Guid id, EmailSuppression suppression, CancellationToken cancellationToken) =>
                await suppression.AllowAgainAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .RequireAuthorization(AdminPolicies.StoreManagement);

        return storeAdmin;
    }
}
