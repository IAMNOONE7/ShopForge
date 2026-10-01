using Microsoft.AspNetCore.Http.HttpResults;
using ShopForge.Infrastructure.Connections;
using ShopForge.Shared.Http;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Api.Connections;

// Which merchant account this storefront takes money through. A credential goes in here and comes out nowhere:
// no endpoint returns one, and the response has nowhere to put one (D-139).
internal static class ProviderConnectionEndpoints
{
    public static IEndpointRouteBuilder MapProviderConnections(this IEndpointRouteBuilder storeAdmin)
    {
        var connections = storeAdmin.MapGroup("/provider-connections");

        connections.MapGet("/", GetConnectionsAsync);
        connections.MapPut("/{provider}", SaveAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        connections.MapPut("/{provider}/secret", SetSecretAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        connections.MapDelete("/{provider}", RemoveAsync).RequireAuthorization(AdminPolicies.StoreManagement);

        return storeAdmin;
    }

    private static async Task<Ok<List<ConnectionResponse>>> GetConnectionsAsync(
        ProviderConnectionAdmin connections,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await connections.AllAsync(cancellationToken)).Select(ConnectionResponse.From).ToList());

    private static async Task<Results<Ok<ConnectionResponse>, ValidationProblem>> SaveAsync(
        string provider,
        ConnectionRequest request,
        ProviderConnectionAdmin connections,
        IEnumerable<IPaymentProvider> payments,
        CancellationToken cancellationToken)
    {
        var key = provider.Trim().ToLowerInvariant();
        var errors = new RequestErrors()
            .Check(payments.Any(candidate => candidate.Key == key), "provider", "This deployment has no such provider.")
            .Check(
                request.MerchantId is { Length: > 0 } merchant && merchant.Trim().Length <= 100,
                "merchantId",
                "A merchant id is required (up to 100 characters).")
            .Check(Enum.TryParse<ProviderEnvironment>(request.Environment, ignoreCase: true, out _), "environment", "Choose Test or Live.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var saved = await connections.SaveAsync(
            key,
            request.MerchantId!.Trim(),
            Enum.Parse<ProviderEnvironment>(request.Environment!, ignoreCase: true),
            request.IsActive,
            cancellationToken);

        return TypedResults.Ok(ConnectionResponse.From(saved));
    }

    private static async Task<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>> SetSecretAsync(
        string provider,
        SecretRequest request,
        ProviderConnectionAdmin connections,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(request.Secret is { Length: > 0 }, "secret", "A secret is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        return await connections.SetSecretAsync(provider, request.Secret!, cancellationToken) switch
        {
            SecretOutcome.Kept => TypedResults.NoContent(),
            SecretOutcome.NoSuchConnection => TypedResults.NotFound(),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This deployment has nowhere to keep a credential",
                detail: "Set the provider's credentials in configuration, or give the deployment a secret store."),
        };
    }

    private static async Task<Results<NoContent, NotFound>> RemoveAsync(
        string provider,
        ProviderConnectionAdmin connections,
        CancellationToken cancellationToken) =>
        await connections.RemoveAsync(provider, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
}

internal sealed record ConnectionRequest(string? MerchantId, string? Environment, bool IsActive);

internal sealed record SecretRequest(string? Secret);

internal sealed record ConnectionResponse(
    string Provider,
    string MerchantId,
    string Environment,
    bool IsActive,
    bool HasSecret,
    DateTimeOffset ChangedAt)
{
    public static ConnectionResponse From(ConnectedProvider connection) => new(
        connection.Provider,
        connection.MerchantId,
        connection.Environment.ToString(),
        connection.IsActive,
        connection.HasSecret,
        connection.ChangedAt);
}
