using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Discounts;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Discounts;

public sealed class DiscountTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_code_takes_its_share_off_every_line_and_off_the_total()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "TENOFF", "Percentage", 10m);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        await AddToCartAsync(shopper, furniture.Products["walnut-chair"], 1);

        using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "tenoff" });
        var cart = await shopper.ReadAsync<CartView>(applied);

        // 100 + 200 = 300, less 10 %.
        Assert.Equal(("TENOFF", 30m), (cart.Discount!.Code, cart.Discount.Amount));
        Assert.Equal(270m, cart.ItemsTotal);
    }

    [Fact]
    public async Task A_code_can_be_taken_off_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "TENOFF", "Percentage", 10m);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "TENOFF" });
        await shopper.ReadAsync<CartView>(applied);

        using var removed = await shopper.DeleteAsync("/api/storefront/cart/discount");
        var cart = await shopper.ReadAsync<CartView>(removed);

        Assert.Null(cart.Discount);
        Assert.Equal(100m, cart.ItemsTotal);
    }

    [Theory]
    [InlineData("NOSUCHCODE", "cannot be used")]
    [InlineData("EXPIRED", "expired")]
    [InlineData("TOOSMALL", "below the minimum")]
    [InlineData("SPENT", "used up")]
    public async Task A_code_that_cannot_be_used_says_why(string code, string expected)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "EXPIRED", "Percentage", 10m, endsAt: DateTimeOffset.UtcNow.AddDays(-1));
        await CreateDiscountAsync(furniture, "TOOSMALL", "Percentage", 10m, minimumOrderAmount: 5_000m);
        await CreateDiscountAsync(furniture, "SPENT", "Percentage", 10m, maxRedemptions: 1);
        await SpendAsync(furniture, "SPENT");
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        using var response = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = code });
        var problem = (await response.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, string.Join(" ", problem.Errors["code"]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Free_shipping_takes_the_shipping_off_and_leaves_the_goods_alone()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "SHIPFREE", "FreeShipping", 0m);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "SHIPFREE" });
        await shopper.ReadAsync<CartView>(applied);

        var order = await PlaceOrderAsync(shopper);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(100m, confirmation.ItemsTotal);
        Assert.Equal(0m, confirmation.ShippingPrice);
        Assert.Equal(100m, confirmation.GrandTotal);
        Assert.Equal(("SHIPFREE", 4.90m), (confirmation.Discount!.Code, confirmation.Discount.Amount));
    }

    // Two VAT rates in one cart is where taking the discount off the total would go wrong (D-085).
    [Fact]
    public async Task The_vat_summary_of_a_discounted_order_adds_up()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lowVat = await furniture.Admin.CreateProductAsync();
        var lowVatListing = await furniture.Admin.ListProductAsync(furniture.Store.StoreId, lowVat, "Reduced Rate Chair", 121m, vatRate: 12m);
        await furniture.Admin.StockAsync(lowVat, 10);
        await CreateDiscountAsync(furniture, "MIXED", "Percentage", 10m);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        await AddToCartAsync(shopper, lowVatListing, 1);
        using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "MIXED" });
        await shopper.ReadAsync<CartView>(applied);

        var order = await PaidOrderAsync(furniture, shopper);
        var invoice = await InvoiceAsync(furniture, order.Number);
        var summary = invoice.VatSummary();

        // 100 at 21 % and 121 at 12 %, less 10 % of 221 = 22.10, plus 4.90 shipping at 21 %.
        Assert.Equal(22.10m, order.DiscountTotal);
        Assert.Equal(invoice.Total, summary.Sum(rate => rate.Gross));
        Assert.Equal(invoice.VatTotal, summary.Sum(rate => rate.Vat));
        Assert.Equal([12m, 21m], summary.Select(rate => rate.Rate));
        Assert.Equal(203.80m, invoice.Total);
    }

    [Fact]
    public async Task Only_one_order_gets_the_last_redemption()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "LASTONE", "Percentage", 10m, maxRedemptions: 1);
        using var first = new StorefrontApi(factory, furniture.Store);
        using var second = new StorefrontApi(factory, furniture.Store);

        foreach (var shopper in new[] { first, second })
        {
            await AddToCartAsync(shopper, furniture.Products["beech-stool"], 1);
            using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "LASTONE" });
            await shopper.ReadAsync<CartView>(applied);
        }

        using var firstOrder = await first.PostAsync("/api/storefront/checkout", Checkout.Request());
        using var secondOrder = await second.PostAsync("/api/storefront/checkout", Checkout.Request());
        var redemptions = await RedemptionsAsync(furniture, "LASTONE");

        Assert.Equal(HttpStatusCode.Created, firstOrder.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondOrder.StatusCode);
        Assert.Equal(1, redemptions);
    }

    // Two checkouts that reach the last redemption at the same moment: the second waits on the row the first one
    // locked and then finds nothing left (D-086). The sequential test above is answered by the cart's own check; this
    // one is answered by the database.
    [Fact]
    public async Task Concurrent_redemptions_cannot_both_take_the_last_one()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "RACEONE", "Percentage", 10m, maxRedemptions: 1);

        await using var firstScope = TestStores.CreateScope(factory.Services, furniture.Store);
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<DbContext>();
        await using var firstTransaction = await firstDbContext.Database.BeginTransactionAsync(CancellationToken);
        var firstCodes = firstScope.ServiceProvider.GetRequiredService<DiscountCodes>();
        var first = await firstCodes.TryRedeemAsync(await firstCodes.FindAsync("RACEONE", CancellationToken) ?? throw new InvalidOperationException(), CancellationToken);

        var second = Task.Run(async () =>
        {
            await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            var codes = scope.ServiceProvider.GetRequiredService<DiscountCodes>();
            var discount = await codes.FindAsync("RACEONE", CancellationToken) ?? throw new InvalidOperationException();
            var redeemed = await codes.TryRedeemAsync(discount, CancellationToken);
            await transaction.CommitAsync(CancellationToken);

            return redeemed;
        });

        await firstTransaction.CommitAsync(CancellationToken);
        var secondResult = await second;

        Assert.True(first);
        Assert.False(secondResult);
        Assert.Equal(1, await RedemptionsAsync(furniture, "RACEONE"));
    }

    [Fact]
    public async Task A_customer_cannot_use_a_one_per_person_code_twice()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "ONEEACH", "Percentage", 10m, maxRedemptionsPerCustomer: 1);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        using var firstApplied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "ONEEACH" });
        await shopper.ReadAsync<CartView>(firstApplied);
        using var firstOrder = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        using var secondApplied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = "ONEEACH" });
        await shopper.ReadAsync<CartView>(secondApplied);
        using var secondOrder = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        Assert.Equal(HttpStatusCode.Created, firstOrder.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondOrder.StatusCode);
    }

    [Fact]
    public async Task A_code_belongs_to_the_store_that_made_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "MINEONLY", "Percentage", 10m);
        var otherProduct = await furniture.Admin.CreateProductAsync();
        var otherListing = await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, otherProduct, "Lamp", 10m);
        await furniture.Admin.StockAsync(otherProduct, 5);
        using var otherShopper = new StorefrontApi(factory, furniture.OtherStore);
        await AddToCartAsync(otherShopper, otherListing, 1);

        using var response = await otherShopper.PutAsync("/api/storefront/cart/discount", new { Code = "MINEONLY" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task CreateDiscountAsync(
        FurnitureStore furniture,
        string code,
        string kind,
        decimal value,
        decimal? minimumOrderAmount = null,
        DateTimeOffset? endsAt = null,
        int? maxRedemptions = null,
        int? maxRedemptionsPerCustomer = null)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/discounts",
            new
            {
                Code = code,
                Name = $"{code} discount",
                Kind = kind,
                Value = value,
                MinimumOrderAmount = minimumOrderAmount,
                StartsAt = (DateTimeOffset?)null,
                EndsAt = endsAt,
                MaxRedemptions = maxRedemptions,
                MaxRedemptionsPerCustomer = maxRedemptionsPerCustomer,
                IsActive = true,
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task SpendAsync(FurnitureStore furniture, string code)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-bench"], 1);
        using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = code });
        await shopper.ReadAsync<CartView>(applied);
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
    }

    private Task<int> RedemptionsAsync(FurnitureStore furniture, string code) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Discount>()
            .Where(discount => discount.Code == code)
            .Select(discount => discount.Redemptions)
            .SingleAsync(CancellationToken));

    private Task<Invoice> InvoiceAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
            .SingleAsync(invoice => invoice.OrderNumber == orderNumber && invoice.Kind == InvoiceKind.Invoice, CancellationToken));

    private Task<Order> OrderAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>()
            .SingleAsync(order => order.Number == orderNumber, CancellationToken));

    private async Task<Order> PaidOrderAsync(FurnitureStore furniture, StorefrontApi shopper)
    {
        var placed = await PlaceOrderAsync(shopper);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{placed.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        await factory.EventuallyAsync(
            () => factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
                .CountAsync(invoice => invoice.OrderNumber == placed.Number, CancellationToken)),
            invoices => invoices > 0,
            CancellationToken);

        return await OrderAsync(furniture, placed.Number);
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private sealed record ProblemView(string Title, Dictionary<string, string[]> Errors);
}
