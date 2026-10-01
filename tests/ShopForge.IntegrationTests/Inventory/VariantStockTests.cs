using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Inventory;

// A shirt in three sizes is three things on a shelf. Each has its own count, a shopper buys one of them by name,
// and what comes back goes on the shelf it came off (D-135).
public sealed class VariantStockTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_size_is_counted_on_its_own_shelf()
    {
        var shirt = await ShirtStore.CreateAsync(factory);

        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 2);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Large, 7);
        var counts = await StockAsync(shirt);

        Assert.Equal(2, counts[shirt.Small].OnHand);
        Assert.Equal(7, counts[shirt.Large].OnHand);
    }

    // The card in the listing adds the sizes up, because that is what the shopper could buy; the product page
    // takes them apart, because that is where one is chosen.
    [Fact]
    public async Task The_listing_adds_the_sizes_up_and_the_page_takes_them_apart()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 2);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Large, 7);

        using var shopper = new StorefrontApi(factory, shirt.Furniture.Store);
        var page = await shopper.GetJsonAsync<ProductPageView>("/api/storefront/products");
        var detail = await shopper.GetJsonAsync<ProductDetailView>($"/api/storefront/products/{shirt.Slug}");

        Assert.Equal(9, page.Items.Single(item => item.Slug == shirt.Slug).Available);
        Assert.Equal(["Size"], detail.OptionNames);
        Assert.Equal([(["S"], 2), (["L"], 7)], detail.Variants.Select(variant => (variant.OptionValues, variant.Available)));
    }

    [Fact]
    public async Task A_shopper_buys_the_size_they_asked_for_and_only_that_one_leaves_the_shelf()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 5);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Large, 5);

        using var shopper = new StorefrontApi(factory, shirt.Furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = shirt.Listing, VariantId = shirt.Large, Quantity = 2 });
        var cart = await shopper.ReadAsync<CartView>(added);
        var order = await CheckoutAsync(shirt, shopper);
        var counts = await StockAsync(shirt);

        Assert.Equal([shirt.Large], cart.Items.Select(line => line.VariantId));
        Assert.Equal([["L"]], cart.Items.Select(line => line.OptionValues));
        Assert.Equal((5, 0), (counts[shirt.Small].OnHand, counts[shirt.Small].Reserved));
        Assert.Equal((5, 2), (counts[shirt.Large].OnHand, counts[shirt.Large].Reserved));
        Assert.Equal([shirt.Large], await SoldFormsAsync(shirt, order.Number));
    }

    // Two sizes of one shirt are two lines of one cart, not one line of two.
    [Fact]
    public async Task Two_sizes_of_one_shirt_are_two_lines()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 5);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Large, 5);

        using var shopper = new StorefrontApi(factory, shirt.Furniture.Store);
        await AddAsync(shopper, shirt.Listing, shirt.Small, 1);
        using var second = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = shirt.Listing, VariantId = shirt.Large, Quantity = 3 });
        var cart = await shopper.ReadAsync<CartView>(second);

        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(4, cart.Count);
        Assert.Equal([(["S"], 1), (["L"], 3)], cart.Items.Select(line => (line.OptionValues, line.Quantity)));
    }

    // Asking for "a shirt" when the shop sells three is not an order anybody can pack.
    [Fact]
    public async Task A_shopper_has_to_say_which_size_they_want()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 5);

        using var shopper = new StorefrontApi(factory, shirt.Furniture.Store);
        using var silent = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = shirt.Listing, Quantity = 1 });
        using var spoken = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = shirt.Listing, VariantId = shirt.Small, Quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, silent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, spoken.StatusCode);
    }

    // A shirt sold in one form is still bought the way everything was bought before variants existed.
    [Fact]
    public async Task A_product_sold_in_one_form_needs_no_saying_which()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        var cart = await shopper.ReadAsync<CartView>(added);

        Assert.Equal([furniture.VariantIds["oak-chair"]], cart.Items.Select(line => line.VariantId));
    }

    // The size that went out is the size that comes back. An order holding two sizes is the only way to tell
    // that apart from "put it back somewhere", which is what a return did before it named the form.
    [Fact]
    public async Task What_comes_back_goes_on_the_shelf_it_came_off()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Small, 5);
        await shirt.Furniture.Admin.VariantStockAsync(shirt.Large, 5);

        using var buyer = await BuyerAsync(shirt);
        await AddAsync(buyer, shirt.Listing, shirt.Small, 1);
        await AddAsync(buyer, shirt.Listing, shirt.Large, 2);
        var order = await CheckoutAsync(shirt, buyer);
        await PayAsync(shirt, order.Number);
        var afterSale = await StockAsync(shirt);

        await ReturnAsync(shirt, buyer, order.Number, shirt.Large, 2);
        var afterReturn = await StockAsync(shirt);

        Assert.Equal((4, 3), (afterSale[shirt.Small].OnHand, afterSale[shirt.Large].OnHand));
        Assert.Equal((4, 5), (afterReturn[shirt.Small].OnHand, afterReturn[shirt.Large].OnHand));
    }

    private async Task ReturnAsync(ShirtStore shirt, StorefrontApi buyer, string orderNumber, Guid variantId, int quantity)
    {
        using var requested = await buyer.PostAsync(
            $"/api/storefront/account/orders/{orderNumber}/returns",
            new { Lines = new[] { new { StoreProductId = shirt.Listing, VariantId = variantId, Quantity = quantity } }, Reason = "Too big." });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var returns = await shirt.Furniture.Admin.GetFromJsonAsync<List<ReturnRow>>(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/returns", CancellationToken);
        var booked = returns!.Single(candidate => candidate.OrderNumber == orderNumber).Id;

        using var accepted = await shirt.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/returns/{booked}/accept", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var received = await shirt.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/returns/{booked}/receive", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
    }

    private async Task<StorefrontApi> BuyerAsync(ShirtStore shirt)
    {
        var email = $"sizes-{Guid.NewGuid():N}@example.test";
        var buyer = new StorefrontApi(factory, shirt.Furniture.Store);

        await buyer.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Robin", LastName = "Sender", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await buyer.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return buyer;
    }

    private async Task<Dictionary<Guid, StockView>> StockAsync(ShirtStore shirt)
    {
        var stock = await shirt.Furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.ToDictionary(item => item.VariantId);
    }

    private async Task<IReadOnlyList<Guid>> SoldFormsAsync(ShirtStore shirt, string number) =>
        await factory.QueryAsync(shirt.Furniture.Store, async dbContext =>
        {
            var order = await dbContext.Set<Order>().AsNoTracking().SingleAsync(candidate => candidate.Number == number, CancellationToken);

            return (IReadOnlyList<Guid>)[.. order.Lines.Select(line => line.VariantId)];
        });

    private static async Task AddAsync(StorefrontApi shopper, Guid listing, Guid variantId, int quantity)
    {
        using var response = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = listing, VariantId = variantId, Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<PlacedOrder> CheckoutAsync(ShirtStore shirt, StorefrontApi shopper)
    {
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
    }

    private async Task PayAsync(ShirtStore shirt, string number)
    {
        using var paid = await shirt.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/orders/{number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
    }

    private sealed record ReturnRow(Guid Id, string OrderNumber);

    private sealed record StockView(Guid VariantId, int OnHand, int Reserved, int Available);

    private sealed record ProductPageView(List<ProductCardView> Items);

    private sealed record ProductCardView(string Slug, int Available);

    private sealed record ProductDetailView(List<string> OptionNames, List<VariantDetailView> Variants);

    private sealed record VariantDetailView(Guid Id, List<string> OptionValues, int Available);

    private sealed record CartView(List<CartLineView> Items, int Count);

    private sealed record CartLineView(Guid StoreProductId, Guid VariantId, List<string> OptionValues, int Quantity);
}
