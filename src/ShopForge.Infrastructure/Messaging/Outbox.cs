using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

internal sealed class Outbox(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : IOutbox
{
    public void Enqueue<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent =>
        dbContext.Add(new OutboxMessage(
            storeContext.StoreId ?? throw new InvalidOperationException("Events belong to a store; none is in scope."),
            storeContext.TenantId!.Value,
            TEvent.EventType,
            JsonSerializer.Serialize(domainEvent, OutboxJson.Options),
            Activity.Current?.Id,
            clock.GetUtcNow()));
}

internal static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
