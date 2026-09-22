using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Payments;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Payments;

public sealed class StripePaymentTests : IDisposable
{
    private const string WebhookSecret = "whsec_shopforge_tests";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeCheckoutSessions _sessions = new();
    private readonly WebApplicationFactory<Program> _withStripe;

    public StripePaymentTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withStripe = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:Stripe:SecretKey", "sk_test_shopforge");
            builder.UseSetting("Payments:Stripe:WebhookSecret", WebhookSecret);
            builder.ConfigureTestServices(services => services.AddSingleton<ICheckoutSessions>(_sessions));
        });
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checkout_sends_the_shopper_to_the_hosted_payment_page()
    {
        var store = await StripeStoreAsync();
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 1);

        var order = await PlaceOrderAsync(shopper);
        var session = _sessions.Last!;

        Assert.Equal($"https://checkout.stripe.test/{order.Number}", order.RedirectUrl);
        Assert.Equal((order.Number, store.Store.StoreId), (session.OrderNumber, session.StoreId));
        Assert.Equal((10490L, "EUR"), (session.AmountInMinorUnits, session.Currency));
        Assert.EndsWith($"/order/{order.Number}?token={order.Token}", session.ReturnUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_event_pays_the_order_and_takes_the_stock_out()
    {
        var store = await StripeStoreAsync();
        var productId = store.ProductIds["oak-chair"];
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 2);
        var order = await PlaceOrderAsync(shopper);

        using var response = await SendEventAsync(StripeEvents.Completed("evt_paid_1", store.Store.StoreId, order.Number, WebhookSecret));
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var stock = await StockAsync(store, productId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Paid", confirmation.Status);
        Assert.Equal((FurnitureStore.StockPerProduct - 2, 0), (stock.OnHand, stock.Reserved));
    }

    [Fact]
    public async Task The_same_event_delivered_twice_changes_nothing()
    {
        var store = await StripeStoreAsync();
        var productId = store.ProductIds["oak-chair"];
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);
        var stripeEvent = StripeEvents.Completed("evt_paid_2", store.Store.StoreId, order.Number, WebhookSecret);

        using var first = await SendEventAsync(stripeEvent);
        using var second = await SendEventAsync(stripeEvent);
        var stock = await StockAsync(store, productId);
        var movements = await MovementsAsync(store, productId);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(FurnitureStore.StockPerProduct - 1, stock.OnHand);
        Assert.Single(movements, movement => movement.Reference == order.Number);
    }

    [Fact]
    public async Task An_event_that_is_not_signed_by_the_provider_is_refused()
    {
        var store = await StripeStoreAsync();
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);
        var (payload, _) = StripeEvents.Completed("evt_forged", store.Store.StoreId, order.Number, WebhookSecret);

        using var response = await SendEventAsync((payload, StripeEvents.Sign(payload, "whsec_someone_else")));
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AwaitingPayment", confirmation.Status);
    }

    [Fact]
    public async Task An_expired_session_cancels_the_order_and_returns_the_stock()
    {
        var store = await StripeStoreAsync();
        var productId = store.ProductIds["oak-chair"];
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 3);
        var order = await PlaceOrderAsync(shopper);

        using var response = await SendEventAsync(StripeEvents.Expired("evt_expired_1", store.Store.StoreId, order.Number, WebhookSecret));
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var stock = await StockAsync(store, productId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", confirmation.Status);
        Assert.Equal((FurnitureStore.StockPerProduct, 0), (stock.OnHand, stock.Reserved));
    }

    [Fact]
    public async Task An_event_for_an_order_the_store_does_not_have_is_not_found()
    {
        var store = await StripeStoreAsync();

        using var response = await SendEventAsync(StripeEvents.Completed("evt_unknown", store.Store.StoreId, "2026-99999", WebhookSecret));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_order_survives_a_provider_that_will_not_answer()
    {
        var store = await StripeStoreAsync();
        using var shopper = new StorefrontApi(_withStripe, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 1);
        _sessions.Fails = true;

        try
        {
            var order = await PlaceOrderAsync(shopper);
            var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

            Assert.Null(order.RedirectUrl);
            Assert.Contains("could not be started", order.PaymentInstructions, StringComparison.Ordinal);
            Assert.Equal("AwaitingPayment", confirmation.Status);
        }
        finally
        {
            _sessions.Fails = false;
        }
    }

    // A store can be set up on a deployment that has Stripe and then run on one that does not.
    [Fact]
    public async Task A_method_whose_provider_is_not_configured_is_not_offered()
    {
        var store = await StripeStoreAsync();
        using var shopper = new StorefrontApi(_factory, store.Store);
        await AddToCartAsync(shopper, store.Products["oak-chair"], 1);

        var methods = await shopper.GetJsonAsync<CheckoutMethodsView>("/api/storefront/checkout/methods");
        using var refused = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "card"));

        Assert.DoesNotContain(methods.PaymentMethods, method => method.Code == "card");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Without_keys_a_store_cannot_offer_stripe()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);

        using var refused = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods",
            new { Name = "Card", ProviderKey = "stripe", IsActive = true },
            CancellationToken);
        var providers = await furniture.Admin.GetFromJsonAsync<List<string>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-providers",
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["manual"], providers);
    }

    public void Dispose() => _withStripe.Dispose();

    private async Task<FurnitureStore> StripeStoreAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        using var admin = await TestUsers.LoginAsync(_withStripe, await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId));

        using var created = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods",
            new { Name = "Card", ProviderKey = "stripe", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var deactivated = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods/bank-transfer",
            new { Name = "Bank transfer", IsActive = false },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        return furniture;
    }

    private async Task<HttpResponseMessage> SendEventAsync((string Payload, string Signature) stripeEvent)
    {
        using var client = _withStripe.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/payments/stripe")
        {
            Content = new StringContent(stripeEvent.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", stripeEvent.Signature);

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<StockView> StockAsync(FurnitureStore store, Guid productId)
    {
        var stock = await store.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.ProductId == productId);
    }

    private async Task<List<MovementView>> MovementsAsync(FurnitureStore store, Guid productId) =>
        (await store.Admin.GetFromJsonAsync<List<MovementView>>($"/api/admin/stock/{productId}/movements", CancellationToken))!;

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrderView> PlaceOrderAsync(StorefrontApi shopper)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "card"));

        return await shopper.ReadAsync<PlacedOrderView>(response, HttpStatusCode.Created);
    }

    private sealed record PlacedOrderView(string Number, Guid Token, string PaymentInstructions, string? RedirectUrl);

    private sealed record StockView(Guid ProductId, int OnHand, int Reserved, int Available);

    private sealed record MovementView(int Quantity, string Reason, string Reference);

    private sealed record CheckoutMethodsView(List<MethodView> PaymentMethods, List<MethodView> ShippingMethods);

    private sealed record MethodView(string Code, string Name);
}
