using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

internal sealed class OutboxMessage : IStoreOwned
{
    // 1, 5, 15, 60 and 360 minutes: a handler that fails because something else is down gets a few chances before the
    // message is put aside for a person to look at (D-068).
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromMinutes(360),
    ];

    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid storeId, Guid tenantId, string type, string payload, string? traceParent, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        TraceParent = traceParent;
        CreatedAt = createdAt;
        DueAt = createdAt;
        Status = OutboxStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid TenantId { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    // The request that caused the event, so the work it triggers stays in the same trace (D-070).
    public string? TraceParent { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public void Succeed(DateTimeOffset now)
    {
        Attempts++;
        Status = OutboxStatus.Processed;
        ProcessedAt = now;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Attempts++;
        Error = error.Length > 1000 ? error[..1000] : error;

        if (Attempts > Backoff.Length)
        {
            Status = OutboxStatus.Failed;
            ProcessedAt = now;

            return;
        }

        DueAt = now + Backoff[Attempts - 1];
    }

    public void Requeue(DateTimeOffset now)
    {
        Status = OutboxStatus.Pending;
        Attempts = 0;
        DueAt = now;
        ProcessedAt = null;
    }
}

internal enum OutboxStatus
{
    Pending,
    Processed,
    Failed,
}
