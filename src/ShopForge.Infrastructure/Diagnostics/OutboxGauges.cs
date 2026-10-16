using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Persistence;

namespace ShopForge.Infrastructure.Diagnostics;

// How far behind the worker is, and what it gave up on: the two questions an alert should be able to ask. Observable
// instruments exist only while something holds them, so this lives for as long as the host does.
internal sealed class OutboxGauges : IHostedService, IDisposable
{
    private readonly Meter _meter;
    private readonly IServiceProvider _services;
    private readonly SchemaGate _schema;

    public OutboxGauges(IMeterFactory meterFactory, IServiceProvider services, SchemaGate schema)
    {
        _services = services;
        _schema = schema;
        _meter = meterFactory.Create(ShopForgeMetrics.MeterName);
        _meter.CreateObservableGauge("shopforge.outbox.pending", () => Count(OutboxStatus.Pending), description: "Messages waiting to be delivered.");
        _meter.CreateObservableGauge("shopforge.outbox.failed", () => Count(OutboxStatus.Failed), description: "Messages that gave up.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();

    // Nothing is measured from a database that has not caught up with the build, whose tables may not be there yet
    // (D-205).
    private IEnumerable<Measurement<long>> Count(OutboxStatus status)
    {
        if (!_schema.IsCaughtUp)
        {
            return [];
        }

        using var scope = _services.CreateScope();

        return
        [
            new Measurement<long>(scope.ServiceProvider.GetRequiredService<DbContext>().Set<OutboxMessage>()
                .IgnoreQueryFilters()
                .LongCount(message => message.Status == status)),
        ];
    }
}
