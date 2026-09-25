using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Diagnostics;

public sealed class ObservabilityTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_answer_carries_the_trace_it_belongs_to()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var response = await shopper.GetAsync("/api/storefront/products");
        var traceId = response.Headers.TryGetValues("X-Trace-Id", out var values) ? values.Single() : null;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^[0-9a-f]{32}$", traceId);
    }

    [Fact]
    public async Task Readiness_covers_the_database_and_the_file_storage()
    {
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready", CancellationToken);
        using var live = await client.GetAsync("/health/live", CancellationToken);
        var checks = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(registration => registration.Tags.Contains("ready"))
            .Select(registration => registration.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(["database", "file-storage"], checks);
    }

    // Placing an order and the e-mail the worker sends afterwards belong to one story (D-070).
    [Fact]
    public async Task The_work_an_order_causes_stays_in_the_order_s_trace()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        var activities = new List<Activity>();
        using var listener = ListenTo("ShopForge", activities);
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: $"trace-{Guid.NewGuid():N}@example.test"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var traceParent = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<string?>($"""
                SELECT trace_parent AS "Value"
                FROM messaging.outbox_messages
                WHERE store_id = {furniture.Store.StoreId} AND payload LIKE {'%' + order.Number + '%'}
                ORDER BY created_at
                LIMIT 1
                """)
            .SingleAsync(CancellationToken));
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.NotNull(traceParent);
        var traceId = TraceIdOf(traceParent);

        // The worker shares this host and may have leased the message before this dispatch, so the activity is
        // waited for: what matters is that whoever handled it stayed in the order's trace.
        var traced = await factory.EventuallyAsync(
            () => Task.FromResult(activities.ToList()),
            recorded => recorded.Any(activity => activity.OperationName == "outbox order.placed" && activity.TraceId.ToString() == traceId),
            CancellationToken);

        Assert.Contains(traced, activity => activity.OperationName == "outbox order.placed" && activity.TraceId.ToString() == traceId);
    }

    [Fact]
    public async Task Placing_an_order_and_refusing_one_are_counted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.StockAsync(furniture.ProductIds["beech-stool"], 1);
        using var first = new StorefrontApi(factory, furniture.Store);
        using var second = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(first, furniture.Products["beech-stool"], 1);
        await AddToCartAsync(second, furniture.Products["beech-stool"], 1);

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        using var meter = ListenToMeter(counts);
        using var placed = await first.PostAsync("/api/storefront/checkout", Checkout.Request());
        using var refused = await second.PostAsync("/api/storefront/checkout", Checkout.Request());
        meter.RecordObservableInstruments();

        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.True(counts.GetValueOrDefault("shopforge.orders.placed") >= 1);
        Assert.True(counts.GetValueOrDefault("shopforge.stock.reservations_refused") >= 1);
        Assert.True(counts.ContainsKey("shopforge.outbox.pending"));
    }

    private static string TraceIdOf(string traceParent) => traceParent.Split('-')[1];

    private static ActivityListener ListenTo(string source, List<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static MeterListener ListenToMeter(Dictionary<string, long> counts)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "ShopForge")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            lock (counts)
            {
                counts[instrument.Name] = counts.GetValueOrDefault(instrument.Name) + measurement;
            }
        });

        listener.Start();

        return listener;
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
