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
        }).RequireRateLimiting(RateLimits.Webhooks);

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

    // What the store's mail goes out through, and a domain of its own to send it from (D-211). Connecting and removing
    // are operations: the answer is the operation, at once, and its steps are under /operations.
    public static IEndpointRouteBuilder MapAdminSendingDomainEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var email = storeAdmin.MapGroup("/email");

        email.MapGet("/", async (SendingDomainAdmin admin, CancellationToken cancellationToken) =>
            TypedResults.Ok(await admin.ViewAsync(cancellationToken)));

        email.MapPost("/domain", async (ConnectSendingDomainRequest request, SendingDomainAdmin admin, CancellationToken cancellationToken) =>
                Answer(await admin.ConnectAsync(request, cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        email.MapDelete("/domain", async (SendingDomainAdmin admin, CancellationToken cancellationToken) =>
                Answer(await admin.RemoveAsync(cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        return storeAdmin;
    }

    private static IResult Answer(SendingDomainChange change) => change switch
    {
        { Found: false } => Results.NotFound(),
        { Errors: { } errors } => errors.ToProblem(),
        { Refusal: { } refusal, OperationId: { } running } => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Not now",
            detail: refusal,
            extensions: new Dictionary<string, object?> { ["operationId"] = running }),
        { Refusal: { } refusal } => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not now", detail: refusal),
        { OperationId: { } started } => Results.Accepted($"operations/{started}", new { operationId = started }),
        _ => Results.NotFound(),
    };
}
