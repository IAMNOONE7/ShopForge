using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Auditing;

// Reading the record. A company's staff read their own entries because the query filter says so; the platform
// names the company it wants, or asks for its own entries, which belong to no company (D-116).
public sealed class AuditReader(DbContext dbContext)
{
    private const int MaxPageSize = 100;

    public Task<IReadOnlyList<AuditRecord>> ForCurrentTenantAsync(AuditQuery query, CancellationToken cancellationToken) =>
        ReadAsync(dbContext.Set<AuditEntry>(), query, cancellationToken);

    public Task<IReadOnlyList<AuditRecord>> ForTenantAsync(Guid tenantId, AuditQuery query, CancellationToken cancellationToken) =>
        ReadAsync(
            dbContext.Set<AuditEntry>().IgnoreQueryFilters([TenancyFilters.Tenant]).Where(entry => entry.TenantId == tenantId),
            query,
            cancellationToken);

    public Task<IReadOnlyList<AuditRecord>> ForThePlatformAsync(AuditQuery query, CancellationToken cancellationToken) =>
        ReadAsync(
            dbContext.Set<AuditEntry>().IgnoreQueryFilters([TenancyFilters.Tenant]).Where(entry => entry.TenantId == null),
            query,
            cancellationToken);

    private static async Task<IReadOnlyList<AuditRecord>> ReadAsync(
        IQueryable<AuditEntry> entries,
        AuditQuery query,
        CancellationToken cancellationToken)
    {
        if (query.StoreId is { } storeId)
        {
            entries = entries.Where(entry => entry.StoreId == storeId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.RecordedAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.RecordedAt < to);
        }

        return await entries
            .AsNoTracking()
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(Math.Max(query.Page - 1, 0) * PageSizeOf(query))
            .Take(PageSizeOf(query))
            .Select(entry => new AuditRecord(
                entry.Id,
                entry.RecordedAt,
                entry.ActorKind.ToString(),
                entry.ActorId,
                entry.ActorName,
                entry.StoreId,
                entry.Action,
                entry.Subject,
                entry.Details,
                entry.IpAddress))
            .ToListAsync(cancellationToken);
    }

    private static int PageSizeOf(AuditQuery query) => Math.Clamp(query.PageSize, 1, MaxPageSize);
}

public sealed record AuditQuery(Guid? StoreId = null, string? Action = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);

public sealed record AuditRecord(
    Guid Id,
    DateTimeOffset RecordedAt,
    string ActorKind,
    Guid? ActorId,
    string? ActorName,
    Guid? StoreId,
    string Action,
    string Subject,
    string? Details,
    string? IpAddress);
