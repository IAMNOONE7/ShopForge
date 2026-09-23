using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopForge.Infrastructure.Messaging;

namespace ShopForge.Infrastructure.Diagnostics;

// How far behind the worker is, and what it gave up on: the two questions an alert should be able to ask. Observable
// instruments exist only while something holds them, so this lives for as long as the host does.
internal sealed class OutboxGauges : IHostedService, IDisposable
{
    private readonly Meter _meter;
    private readonly IServiceProvider _services;

    public OutboxGauges(IMeterFactory meterFactory, IServiceProvider services)
    {
        _services = services;
        _meter = meterFactory.Create(ShopForgeMetrics.MeterName);
        _meter.CreateObservableGauge("shopforge.outbox.pending", () => Count(OutboxStatus.Pending), description: "Messages waiting to be delivered.");
        _meter.CreateObservableGauge("shopforge.outbox.failed", () => Count(OutboxStatus.Failed), description: "Messages that gave up.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();

    private long Count(OutboxStatus status)
    {
        using var scope = _services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<DbContext>().Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .LongCount(message => message.Status == status);
    }
}
