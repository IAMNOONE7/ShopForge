using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

internal sealed class Outbox(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : IOutbox
{
    // A shop's event still insists on a shop: enqueueing one with nothing in scope would deliver it to nobody, and
    // failing here is how that mistake gets found.
    public void Enqueue<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent =>
        Add(
            storeContext.StoreId ?? throw new InvalidOperationException("Events belong to a store; none is in scope."),
            storeContext.TenantId!.Value,
            domainEvent);

    public void EnqueueOutsideStore<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent =>
        Add(null, storeContext.TenantId, domainEvent);

    private void Add<TEvent>(Guid? storeId, Guid? tenantId, TEvent domainEvent)
        where TEvent : IDomainEvent =>
        dbContext.Add(new OutboxMessage(
            storeId,
            tenantId,
            TEvent.EventType,
            JsonSerializer.Serialize(domainEvent, OutboxJson.Options),
            Activity.Current?.Id,
            clock.GetUtcNow()));
}

internal static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
