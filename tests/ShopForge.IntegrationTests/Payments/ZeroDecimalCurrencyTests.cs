using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Payments;

// A shop in Icelandic krónur, which have no minor unit. Everything here used to be wrong by a factor of a
// hundred, or refused outright, because two decimals were taken for granted (D-186).
public sealed class ZeroDecimalCurrencyTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;

    public ZeroDecimalCurrencyTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withGateway = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var gateway = new FakeGateway();
            services.AddSingleton<IPaymentProvider>(gateway);
            services.AddSingleton<IPaymentNotifications>(gateway);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withGateway.Dispose();

    [Fact]
    public async Task A_price_with_decimals_the_currency_has_not_got_is_refused()
    {
        var (store, admin) = await KronaStoreAsync();
        var productId = await admin.CreateProductAsync();

        using var response = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/products",
            new { ProductId = productId, Name = "Oak Chair", Price = 2495.50m, VatRate = 24m, IsVisible = true, SortOrder = 0 },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 0 decimals", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // The gateway is told a whole number of krónur, not a hundred times too many.
    [Fact]
    public async Task The_gateway_is_charged_in_whole_units()
    {
        var order = await OrderAsync();
        var attempt = await AttemptAsync(order.Store, order.Number);

        Assert.Equal("ISK", attempt.Currency.Code);
        Assert.Equal(2500m, attempt.Amount);
        Assert.Equal(2500L, attempt.Currency.ToMinorUnits(attempt.Amount));
    }

    // 24 % out of 2495 krónur is 482.90 and 21 % out of the 5 for the courier is 0.87, and neither is money here:
    // each has to round to a whole króna, which makes 483 and 1.
    [Fact]
    public async Task The_tax_on_an_order_is_a_whole_number()
    {
        var order = await OrderAsync();
        var row = await _factory.QueryAsync(order.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleAsync(candidate => candidate.Number == order.Number, CancellationToken));

        Assert.Equal(2500m, row.GrandTotal);
        Assert.Equal(decimal.Truncate(row.VatTotal), row.VatTotal);
        Assert.Equal(484m, row.VatTotal);
    }

    private async Task<(TestStore Store, HttpClient Admin)> KronaStoreAsync()
    {
        var store = await TestStores.CreateInCurrencyAsync(_factory.Services, "ISK");
        var admin = await TestUsers.LoginAsync(_withGateway, await TestUsers.CreateAsync(_factory.Services, store.TenantId));

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/payment-methods",
            new { Name = "Gateway", ProviderKey = FakeGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        return (store, admin);
    }

    private async Task<PlacedKronaOrder> OrderAsync()
    {
        var (store, admin) = await KronaStoreAsync();
        var productId = await admin.CreateProductAsync();
        var storeProductId = await admin.ListProductAsync(store.StoreId, productId, "Oak Chair", 2495m, vatRate: 24m);
        await admin.StockAsync(productId, 10);

        using var shopper = new StorefrontApi(_withGateway, store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "gateway"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        return new PlacedKronaOrder(store, order.Number);
    }

    private async Task<PaymentAttempt> AttemptAsync(TestStore store, string orderNumber) =>
        await _factory.QueryAsync(store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(attempt => attempt.OrderNumber == orderNumber, CancellationToken));

    private sealed record PlacedKronaOrder(TestStore Store, string Number);
}
