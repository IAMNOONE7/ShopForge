using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Http;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Idempotency;

internal sealed class IdempotentRequests(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : IIdempotentRequests
{
    public async Task<IdempotencyClaim> ClaimAsync(string endpoint, string key, string fingerprint, CancellationToken cancellationToken)
    {
        if (storeContext.StoreId is not { } storeId || storeContext.TenantId is not { } tenantId)
        {
            return new IdempotencyClaim(IdempotencyVerdict.Claimed);
        }

        // Claiming is one statement, so two requests arriving together cannot both believe they were first: the
        // unique index decides it, and the loser reads whatever the winner has written so far (D-131).
        var claimed = await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO idempotency.requests (id, store_id, tenant_id, endpoint, key, fingerprint, claimed_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})
            ON CONFLICT (store_id, endpoint, key) DO NOTHING
            """,
            [Guid.CreateVersion7(), storeId, tenantId, Truncated(endpoint), key, fingerprint, clock.GetUtcNow()],
            cancellationToken);

        if (claimed == 1)
        {
            return new IdempotencyClaim(IdempotencyVerdict.Claimed);
        }

        var existing = await FindAsync(endpoint, key, cancellationToken);

        if (existing is null)
        {
            return new IdempotencyClaim(IdempotencyVerdict.Claimed);
        }

        if (existing.Fingerprint != fingerprint)
        {
            return new IdempotencyClaim(IdempotencyVerdict.Mismatch);
        }

        return existing is { AnsweredAt: not null, StatusCode: { } statusCode, Body: { } body }
            ? new IdempotencyClaim(IdempotencyVerdict.Replay, new IdempotentResponse(statusCode, existing.ContentType, existing.Location, body))
            : new IdempotencyClaim(IdempotencyVerdict.InFlight);
    }

    public async Task CompleteAsync(string endpoint, string key, IdempotentResponse response, CancellationToken cancellationToken)
    {
        if (await FindAsync(endpoint, key, cancellationToken) is not { } request)
        {
            return;
        }

        request.Answer(response.StatusCode, response.ContentType, response.Location, response.Body, clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string endpoint, string key, CancellationToken cancellationToken) =>
        await dbContext.Set<IdempotentRequest>()
            .Where(request => request.Endpoint == Truncated(endpoint) && request.Key == key && request.AnsweredAt == null)
            .ExecuteDeleteAsync(cancellationToken);

    private Task<IdempotentRequest?> FindAsync(string endpoint, string key, CancellationToken cancellationToken) =>
        dbContext.Set<IdempotentRequest>()
            .SingleOrDefaultAsync(request => request.Endpoint == Truncated(endpoint) && request.Key == key, cancellationToken);

    private static string Truncated(string endpoint) =>
        endpoint.Length <= IdempotentRequest.MaxEndpointLength ? endpoint : endpoint[..IdempotentRequest.MaxEndpointLength];
}
