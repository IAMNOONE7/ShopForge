using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Security;

namespace ShopForge.Api.Provisioning;

// A store's operations: what each step is doing and why one stopped, for anyone in the store to read, and the three
// things a manager can do about it (D-210).
internal static class AdminOperationEndpoints
{
    public static IEndpointRouteBuilder MapAdminOperationEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var operations = storeAdmin.MapGroup("/operations");

        operations.MapGet("/", async (OperationAdmin admin, CancellationToken cancellationToken) =>
            TypedResults.Ok(await admin.ListAsync(cancellationToken)));

        operations.MapGet("/{operationId:guid}", async (Guid operationId, OperationAdmin admin, CancellationToken cancellationToken) =>
            await admin.FindAsync(operationId, cancellationToken) is { } operation ? Results.Ok(operation) : Results.NotFound());

        operations.MapPost("/{operationId:guid}/retry", (Guid operationId, OperationAdmin admin, CancellationToken cancellationToken) =>
                Answer(admin.RetryAsync(operationId, cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        operations.MapPost("/{operationId:guid}/continue", (Guid operationId, OperationAdmin admin, CancellationToken cancellationToken) =>
                Answer(admin.ContinueAsync(operationId, cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        operations.MapPost("/{operationId:guid}/cancel", (Guid operationId, OperationAdmin admin, CancellationToken cancellationToken) =>
                Answer(admin.CancelAsync(operationId, cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        return storeAdmin;
    }

    private static async Task<IResult> Answer(Task<OperationActionResult> acting) => await acting switch
    {
        { Found: false } => Results.NotFound(),
        { Refusal: { } refusal } => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not now", detail: refusal),
        { Operation: { } operation } => Results.Ok(operation),
        _ => Results.NotFound(),
    };
}
