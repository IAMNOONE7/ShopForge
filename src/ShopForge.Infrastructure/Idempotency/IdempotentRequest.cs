using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Idempotency;

// One row per key a caller used, holding the answer the first request gave. It belongs to the store the request
// was made in, so one shop's keys are invisible to another's exactly as its orders are.
internal sealed class IdempotentRequest : IStoreOwned
{
    public const int MaxEndpointLength = 200;

    private IdempotentRequest()
    {
    }

    public IdempotentRequest(Guid storeId, Guid tenantId, string endpoint, string key, string fingerprint, DateTimeOffset claimedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        TenantId = tenantId;
        Endpoint = endpoint;
        Key = key;
        Fingerprint = fingerprint;
        ClaimedAt = claimedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid TenantId { get; private set; }

    // The method and route the key was used on, so one key can be used once per endpoint rather than once ever.
    public string Endpoint { get; private set; } = null!;

    public string Key { get; private set; } = null!;

    public string Fingerprint { get; private set; } = null!;

    public DateTimeOffset ClaimedAt { get; private set; }

    // Null until the first request has answered. A row in that state is a request still in flight.
    public DateTimeOffset? AnsweredAt { get; private set; }

    public int? StatusCode { get; private set; }

    public string? ContentType { get; private set; }

    public string? Location { get; private set; }

    public string? Body { get; private set; }

    public void Answer(int statusCode, string? contentType, string? location, string body, DateTimeOffset answeredAt)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Location = location;
        Body = body;
        AnsweredAt = answeredAt;
    }
}
