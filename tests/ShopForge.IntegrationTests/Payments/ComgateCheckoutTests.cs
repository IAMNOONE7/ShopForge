using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// Taking a payment through Comgate's hosted page. Nothing here reaches Comgate: the client is a stand-in, which
// is the only way to exercise a gateway nobody has an account for yet (D-063's rule, applied to payments).
public sealed class ComgateCheckoutTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public ComgateCheckoutTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withComgate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IComgatePayments>(_comgate);
            services.AddSingleton<ISecretStore>(new InMemorySecrets());
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withComgate.Dispose();

    [Fact]
    public async Task A_shopper_is_sent_to_the_address_comgate_gave_and_the_attempt_remembers_it()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");

        var order = await OrderAsync(store);
        var attempt = await AttemptAsync(store, order.Number);

        Assert.Equal($"https://pay.comgate.test/{order.Number}/1", order.RedirectUrl);
        Assert.Equal(order.RedirectUrl, attempt.RedirectUrl);
        Assert.StartsWith("trans-", attempt.Reference!, StringComparison.Ordinal);
        Assert.Equal(ComgatePaymentProvider.ProviderKey, attempt.Provider);
    }

    // Two chairs at 100 and 4.90 for the courier, less a tenth off the chairs: the order's own total, in the
    // smallest unit, worked out on the server.
    [Fact]
    public async Task The_price_sent_is_the_order_s_total_in_minor_units()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");
        await DiscountAsync(store);

        var order = await OrderAsync(store, discount: "TENOFF");
        var stored = await OrderRowAsync(store, order.Number);

        Assert.Equal(18490L, _comgate.LastPayment!.PriceInMinorUnits);
        Assert.Equal(184.90m, stored.GrandTotal);
        Assert.Equal("EUR", _comgate.LastPayment.Currency);
    }

    [Fact]
    public async Task The_payment_is_taken_through_the_store_s_own_merchant_and_its_environment()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");

        await OrderAsync(store);

        Assert.Equal("MERCHANT-ONE", _comgate.LastMerchant!.MerchantId);
        Assert.Equal(Secret, _comgate.LastMerchant.Secret);
        Assert.True(_comgate.LastPayment!.Test);
    }

    [Fact]
    public async Task A_parcel_going_to_a_locker_is_a_collection_as_far_as_the_gateway_is_concerned()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");

        await OrderAsync(store);
        var toAddress = _comgate.LastPayment!.Delivery;

        var point = await PickupPointAsync(store);
        await OrderAsync(store, shipping: "collection", pickupPoint: point);

        Assert.Equal("HOME_DELIVERY", toAddress);
        Assert.Equal("PICKUP", _comgate.LastPayment!.Delivery);
    }

    // The order is committed before the gateway is called, so a gateway that will not answer costs the shopper
    // their payment page and not their order.
    [Fact]
    public async Task A_gateway_that_will_not_answer_still_leaves_the_shopper_an_order()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");
        _comgate.Fails = true;

        try
        {
            var order = await OrderAsync(store);
            var stored = await OrderRowAsync(store, order.Number);

            Assert.Null(order.RedirectUrl);
            Assert.Contains("could not be started", order.PaymentInstructions, StringComparison.Ordinal);
            Assert.Equal(OrderStatus.AwaitingPayment, stored.Status);
        }
        finally
        {
            _comgate.Fails = false;
        }
    }

    // A shopper who gives up and orders again is a second payment at the gateway, not a reused one.
    [Fact]
    public async Task A_second_order_is_a_second_payment_at_the_gateway()
    {
        var store = await ConnectedStoreAsync("MERCHANT-ONE");

        var first = await OrderAsync(store);
        var second = await OrderAsync(store);
        var attempts = await AttemptsAsync(store);

        Assert.NotEqual(first.RedirectUrl, second.RedirectUrl);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(2, attempts.Select(attempt => attempt.Reference).Distinct().Count());
    }

    [Fact]
    public async Task A_store_that_has_not_connected_comgate_is_not_offered_it()
    {
        var store = await StoreAsync();
        await MethodAsync(store);

        using var shopper = new StorefrontApi(_withComgate, store.Store);
        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");

        Assert.DoesNotContain(methods.PaymentMethods, method => method.Code == "comgate");
    }

    private async Task<FurnitureStore> ConnectedStoreAsync(string merchantId)
    {
        var store = await StoreAsync();
        using var admin = await AdminAsync(store);

        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}",
            new { MerchantId = merchantId, Environment = "Test", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}/secret",
            new { Secret },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);

        await MethodAsync(store);

        return store;
    }

    private Task<FurnitureStore> StoreAsync() => FurnitureStore.CreateAsync(_factory);

    private async Task<HttpClient> AdminAsync(FurnitureStore store) =>
        await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, store.Store.TenantId));

    private async Task MethodAsync(FurnitureStore store)
    {
        using var admin = await AdminAsync(store);
        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/payment-methods",
            new { Name = "Comgate", ProviderKey = ComgatePaymentProvider.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);
    }

    private async Task DiscountAsync(FurnitureStore store)
    {
        using var admin = await AdminAsync(store);
        using var created = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/discounts",
            new { Code = "TENOFF", Name = "Ten off", Kind = "Percentage", Value = 10m },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private async Task<string> PickupPointAsync(FurnitureStore store)
    {
        using var admin = await AdminAsync(store);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/shipping-methods",
            new { Name = "Collection", ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        using var created = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/pickup-points",
            new { Name = "The counter", Line1 = "1 High Street", City = "Brno", PostalCode = "60200", Country = "CZ", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var points = await admin.GetFromJsonAsync<List<PointView>>(
            $"/api/admin/stores/{store.Store.StoreId}/pickup-points", CancellationToken);

        return points!.Single().Code;
    }

    private async Task<PlacedOrderView> OrderAsync(
        FurnitureStore store,
        string? discount = null,
        string shipping = "courier",
        string? pickupPoint = null)
    {
        using var shopper = new StorefrontApi(_withComgate, store.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        if (discount is not null)
        {
            using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = discount });
            Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        }

        using var placed = await shopper.PostAsync(
            "/api/storefront/checkout", Checkout.Request(payment: "comgate", shipping: shipping, pickupPoint: pickupPoint));

        return await shopper.ReadAsync<PlacedOrderView>(placed, HttpStatusCode.Created);
    }

    private async Task<PaymentAttempt> AttemptAsync(FurnitureStore store, string orderNumber) =>
        (await AttemptsAsync(store)).Single(attempt => attempt.OrderNumber == orderNumber);

    private Task<List<PaymentAttempt>> AttemptsAsync(FurnitureStore store) =>
        _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .OrderBy(attempt => attempt.StartedAt)
            .ToListAsync(CancellationToken));

    private Task<Order> OrderRowAsync(FurnitureStore store, string orderNumber) =>
        _factory.QueryAsync(store.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == orderNumber, CancellationToken));

    private sealed record PlacedOrderView(string Number, Guid Token, string PaymentInstructions, string? RedirectUrl);

    private sealed record MethodsView(List<MethodView> PaymentMethods);

    private sealed record MethodView(string Code, string Name);

    private sealed record PointView(string Code, string Name);
}
