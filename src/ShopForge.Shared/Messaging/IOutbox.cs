namespace ShopForge.Shared.Messaging;

// Events are written with the change that caused them and delivered afterwards (D-065): enqueueing only adds a row to
// the current transaction, so nothing is published for work that was rolled back.
public interface IOutbox
{
    void Enqueue<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent;
}

public interface IDomainEvent
{
    static abstract string EventType { get; }
}

// Handlers run outside the request that produced the event, inside the store scope the message names. Delivery is
// at-least-once, so a handler must end in the same state when it runs twice.
public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
