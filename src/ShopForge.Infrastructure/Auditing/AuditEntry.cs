namespace ShopForge.Infrastructure.Auditing;

// Append-only: rows are added and never changed or removed, which the save guard enforces rather than trusting
// every future endpoint to leave them alone (D-116). Like the outbox, it belongs to a company loosely — the
// platform's own actions belong to no tenant at all (D-111).
internal sealed class AuditEntry
{
    public const int MaxDetailsLength = 2000;

    private AuditEntry()
    {
    }

    public AuditEntry(
        Guid? tenantId,
        Guid? storeId,
        AuditActorKind actorKind,
        Guid? actorId,
        string? actorName,
        string action,
        string subject,
        string? details,
        string? ipAddress,
        DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        ActorKind = actorKind;
        ActorId = actorId;
        ActorName = actorName;
        Action = action;
        Subject = subject;
        Details = details is { Length: > MaxDetailsLength } ? details[..MaxDetailsLength] : details;
        IpAddress = ipAddress;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public Guid? StoreId { get; private set; }

    public AuditActorKind ActorKind { get; private set; }

    public Guid? ActorId { get; private set; }

    // Kept as it was at the time: an entry has to stay readable after the person is renamed or deactivated.
    public string? ActorName { get; private set; }

    public string Action { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    public string? Details { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}

internal enum AuditActorKind
{
    System,
    PlatformUser,
    TenantUser,
    Customer,
}
