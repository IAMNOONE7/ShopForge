using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Messaging;

namespace ShopForge.Infrastructure.Messaging;

// Events are declared in Shared (D-066), so the stored type name maps back to a record by looking there once at
// startup: nothing has to be registered by hand, and a message whose type is unknown fails loudly instead of vanishing.
internal sealed class OutboxEventTypes
{
    private readonly Dictionary<string, Delivery> _events;

    public OutboxEventTypes()
        : this(typeof(IDomainEvent).Assembly)
    {
    }

    internal OutboxEventTypes(Assembly eventAssembly) =>
        _events = eventAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToDictionary(NameOf, type => new Delivery(type, InvokerFor(type)), StringComparer.Ordinal);

    public Task DeliverAsync(IServiceProvider provider, string type, string payload, CancellationToken cancellationToken)
    {
        if (!_events.TryGetValue(type, out var delivery))
        {
            throw new InvalidOperationException($"Message type '{type}' is not a known event.");
        }

        var domainEvent = JsonSerializer.Deserialize(payload, delivery.EventType, OutboxJson.Options)
            ?? throw new InvalidOperationException($"A '{type}' message could not be read.");

        return delivery.Invoke(provider, domainEvent, cancellationToken);
    }

    private static string NameOf(Type eventType) =>
        (string)eventType.GetProperty(nameof(IDomainEvent.EventType), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static Func<IServiceProvider, object, CancellationToken, Task> InvokerFor(Type eventType) =>
        (Func<IServiceProvider, object, CancellationToken, Task>)typeof(OutboxEventTypes)
            .GetMethod(nameof(Invoker), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .Invoke(null, null)!;

    private static Func<IServiceProvider, object, CancellationToken, Task> Invoker<TEvent>()
        where TEvent : IDomainEvent =>
        async (provider, domainEvent, cancellationToken) =>
        {
            foreach (var handler in provider.GetServices<IEventHandler<TEvent>>())
            {
                await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
            }
        };

    private sealed record Delivery(Type EventType, Func<IServiceProvider, object, CancellationToken, Task> Invoke);
}
