using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Payments;

// A gateway that asks who is paying and how the parcel reaches them can be told, rather than guessing. What it
// is told is the order's own facts, taken on the server: nothing here comes from the browser.
public sealed class PaymentRequestTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly FakeGateway _gateway = new();
    private readonly WebApplicationFactory<Program> _withGateway;

    public PaymentRequestTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withGateway = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPaymentProvider>(_gateway);
            services.AddSingleton<IPaymentNotifications>(_gateway);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withGateway.Dispose();

    [Fact]
    public async Task A_gateway_is_told_who_is_paying_and_where_they_are()
    {
        var furniture = await GatewayStoreAsync();

        var order = await OrderAsync(furniture, Checkout.Request(payment: "gateway"));

        Assert.Equal(order.Number, _gateway.Last!.OrderNumber);
        Assert.Equal("Alex Buyer", _gateway.Last.CustomerName);
        Assert.Equal("IE", _gateway.Last.CountryCode);
        Assert.Equal("en", _gateway.Last.Language);
        Assert.Equal(204.90m, _gateway.Last.Amount);
    }

    // A locker is a locker because this order is going to one, which some gateways need to know.
    [Fact]
    public async Task A_parcel_going_to_a_pickup_point_says_so()
    {
        var furniture = await GatewayStoreAsync();
        var point = await PickupPointAsync(furniture);

        await OrderAsync(furniture, Checkout.Request(payment: "gateway"));
        var toAddress = _gateway.Last!.Delivery;

        await OrderAsync(furniture, Checkout.Request(payment: "gateway", shipping: "collection", pickupPoint: point));
        var toPoint = _gateway.Last!.Delivery;

        Assert.Equal(PaymentDelivery.ToAddress, toAddress);
        Assert.Equal(PaymentDelivery.ToPickupPoint, toPoint);
    }

    // The gateway is told what the order came to, whatever the browser believed it would be.
    [Fact]
    public async Task The_amount_is_the_order_s_own_and_not_the_browser_s()
    {
        var furniture = await GatewayStoreAsync();

        var order = await OrderAsync(furniture, Checkout.Request(payment: "gateway"));
        var stored = await _factory.QueryAsync(furniture.Store, async dbContext =>
            (await dbContext.Set<ShopForge.Orders.Domain.Order>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Number == order.Number, CancellationToken)).GrandTotal);

        Assert.Equal(stored, _gateway.Last!.Amount);
    }

    private async Task<FurnitureStore> GatewayStoreAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        var user = await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId);
        using var admin = await TestUsers.LoginAsync(_withGateway, user);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods",
            new { Name = "Gateway", ProviderKey = FakeGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        return furniture;
    }

    // The fixture's store has one shipping method and it needs no point, so this makes one that does.
    private async Task<string> PickupPointAsync(FurnitureStore furniture)
    {
        var user = await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId);
        using var admin = await TestUsers.LoginAsync(_withGateway, user);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods",
            new { Name = "Collection", ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        using var created = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pickup-points",
            new { Name = "The counter", Line1 = "1 High Street", City = "Brno", PostalCode = "60200", Country = "CZ", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var points = await admin.GetFromJsonAsync<List<PointView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/pickup-points", CancellationToken);

        return points!.Single().Code;
    }

    private async Task<PlacedOrder> OrderAsync(FurnitureStore furniture, object checkout)
    {
        using var shopper = new StorefrontApi(_withGateway, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", checkout);

        return await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
    }

    private sealed record PointView(string Code, string Name);
}
