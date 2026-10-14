using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Payments;

// A notification and the sweep that goes asking reach the same payment at the same moment, a thousand times over.
// Whichever arrives second must find the first one's answer, so each order is paid once, its stock taken once, and
// its shopper told once (Roadmap V2 §4.3, item 4; D-176, D-199).
public sealed class PaymentRaceTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";
    private const int Races = 1000;

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly InMemorySecrets _secrets = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public PaymentRaceTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withComgate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IComgatePayments>(_comgate);
            services.AddSingleton<ISecretStore>(_secrets);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withComgate.Dispose();

    [Fact]
    public async Task A_push_and_a_sweep_that_meet_on_one_payment_pay_it_once_a_thousand_times_over()
    {
        var store = await ConnectedStoreAsync();
        var variantId = await store.Admin.DefaultVariantIdAsync(store.ProductIds["oak-chair"]);
        using var raised = await store.Admin.SetVariantStockAsync(variantId, Races * 2);
        var attempts = new List<PaymentAttempt>();

        for (var order = 0; order < Races; order++)
        {
            attempts.Add(await OrderedAsync(store));
        }

        foreach (var attempt in attempts)
        {
            _comgate.Answers[attempt.Reference!] = new ComgateTransaction(
                attempt.Reference!, "PAID", Currency.Of("EUR").ToMinorUnits(attempt.Amount), "EUR", attempt.OrderNumber, Test: true);
        }

        var confirmed = 0L;
        var meters = _withComgate.Services.GetRequiredService<IMeterFactory>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "shopforge.payments.confirmed" && ReferenceEquals(instrument.Meter.Scope, meters))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref confirmed, measurement));
        listener.Start();

        foreach (var attempt in attempts)
        {
            var push = Task.Run(() => PushAsync(store, attempt), CancellationToken);
            var sweep = Task.Run(() => SweepAsync(store, attempt), CancellationToken);

            await SettledAsync(push);
            await SettledAsync(sweep);
        }

        var numbers = attempts.Select(attempt => attempt.OrderNumber).ToList();
        var paid = await _factory.QueryAsync(store.Store, dbContext => dbContext.Set<Order>().AsNoTracking()
            .CountAsync(order => numbers.Contains(order.Number) && order.Status == OrderStatus.Paid, CancellationToken));
        var answers = await _factory.QueryAsync(store.Store, dbContext => dbContext.Set<PaymentEvent>().AsNoTracking()
            .Where(recorded => numbers.Contains(recorded.OrderNumber))
            .GroupBy(recorded => recorded.OrderNumber)
            .Select(group => group.Count())
            .ToListAsync(CancellationToken));
        var sales = await PerOrderAsync(
            store,
            $"SELECT reference AS \"Value\" FROM inventory.stock_movements WHERE variant_id = {variantId} AND reason = 'Sale'");
        var letters = await PerOrderAsync(
            store,
            $"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {store.Store.StoreId} AND type = 'order.paid'");

        Assert.Equal(Races, paid);
        Assert.Equal(Enumerable.Repeat(1, Races), answers);
        Assert.All(numbers, number => Assert.Equal(1, sales.Count(reference => reference == number)));
        Assert.All(numbers, number => Assert.Equal(1, letters.Count(payload => payload.Contains($"\"{number}\"", StringComparison.Ordinal))));
        Assert.Equal(Races, Interlocked.Read(ref confirmed));
    }

    // The one that loses the race for the answer's record is turned away — a 500 that Comgate retries, a sweep that
    // asks again on its next pass. What matters is what is left afterwards, which the test asserts.
    private static async Task SettledAsync(Task race)
    {
        try
        {
            await race;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async Task<List<string>> PerOrderAsync(FurnitureStore store, FormattableString query) =>
        await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Database.SqlQuery<string>(query).ToListAsync(CancellationToken));

    private async Task PushAsync(FurnitureStore store, PaymentAttempt attempt)
    {
        using var client = _withComgate.CreateClient();
        using var pushed = await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = MerchantOf(store), transId = attempt.Reference },
            CancellationToken);
    }

    // What the sweep does for one attempt, through the same two calls: ask the gateway, record the answer (D-176).
    private async Task SweepAsync(FurnitureStore store, PaymentAttempt attempt)
    {
        await using var scope = _withComgate.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.Store.StoreId, store.Store.TenantId);

        var comgate = scope.ServiceProvider.GetServices<IPaymentEnquiries>().Single(enquiry => enquiry.Key == ComgatePaymentProvider.ProviderKey);
        var answer = await comgate.AskAsync(
            new PaymentEnquiry(store.Store.StoreId, attempt.OrderNumber, attempt.Reference!, attempt.Amount, attempt.Currency),
            CancellationToken);

        await scope.ServiceProvider.GetRequiredService<PaymentResults>().RecordAsync(ComgatePaymentProvider.ProviderKey, answer!, CancellationToken);
    }

    private static string MerchantOf(FurnitureStore store) => $"MERCHANT-{store.Store.StoreId:N}"[..20];

    private async Task<PaymentAttempt> OrderedAsync(FurnitureStore store)
    {
        using var shopper = new StorefrontApi(_withComgate, store.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        return await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));
    }

    private async Task<FurnitureStore> ConnectedStoreAsync()
    {
        var store = await FurnitureStore.CreateAsync(_factory);
        using var admin = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, store.Store.TenantId));

        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}",
            new { MerchantId = MerchantOf(store), Environment = "Test", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}/secret",
            new { Secret },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/payment-methods",
            new { Name = "Comgate", ProviderKey = ComgatePaymentProvider.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        return store;
    }
}
