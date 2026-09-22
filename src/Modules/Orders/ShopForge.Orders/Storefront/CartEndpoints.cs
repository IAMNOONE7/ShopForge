using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Storefront;

internal static class CartEndpoints
{
    public static void MapCart(this IEndpointRouteBuilder storefront)
    {
        var cart = storefront.MapGroup("/cart");

        cart.MapGet("/", GetCartAsync);
        cart.MapPost("/items", AddItemAsync);
        cart.MapPut("/items/{storeProductId:guid}", SetQuantityAsync);
        cart.MapDelete("/items/{storeProductId:guid}", RemoveItemAsync);
    }

    private static async Task<Ok<CartResponse>> GetCartAsync(
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, clock);
        var cart = await carts.FindAsync(cancellationToken);

        return TypedResults.Ok(cart is null
            ? CartResponse.Empty
            : CartResponse.From(await carts.ContentsAsync(cart, cancellationToken)));
    }

    private static async Task<Results<Ok<CartResponse>, ValidationProblem>> AddItemAsync(
        AddItemRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var quantity = request.Quantity ?? 1;
        var errors = new RequestErrors().Check(quantity is > 0 and <= Cart.MaxQuantity, "quantity", $"Quantity must be between 1 and {Cart.MaxQuantity}.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if ((await products.FindAsync([request.StoreProductId], cancellationToken)).Count == 0)
        {
            return new RequestErrors().Check(false, "storeProductId", "The product is not available in this store.").ToProblem();
        }

        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, clock);
        var cart = await carts.GetOrCreateAsync(cancellationToken);
        cart.Add(request.StoreProductId, quantity);
        carts.Touch(cart);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CartResponse.From(await carts.ContentsAsync(cart, cancellationToken)));
    }

    private static async Task<Results<Ok<CartResponse>, ValidationProblem, NotFound>> SetQuantityAsync(
        Guid storeProductId,
        SetQuantityRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(request.Quantity is >= 0 and <= Cart.MaxQuantity, "quantity", $"Quantity must be between 0 and {Cart.MaxQuantity}.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        return await ChangeAsync(
            httpContext,
            dbContext,
            storeContext,
            products,
            stock,
            clock,
            cart => cart.SetQuantity(storeProductId, request.Quantity),
            cancellationToken);
    }

    private static Task<Results<Ok<CartResponse>, ValidationProblem, NotFound>> RemoveItemAsync(
        Guid storeProductId,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(httpContext, dbContext, storeContext, products, stock, clock, cart => cart.SetQuantity(storeProductId, 0), cancellationToken);

    private static async Task<Results<Ok<CartResponse>, ValidationProblem, NotFound>> ChangeAsync(
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        TimeProvider clock,
        Action<Cart> change,
        CancellationToken cancellationToken)
    {
        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, clock);
        var cart = await carts.FindAsync(cancellationToken);

        if (cart is null)
        {
            return TypedResults.NotFound();
        }

        change(cart);
        carts.Touch(cart);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CartResponse.From(await carts.ContentsAsync(cart, cancellationToken)));
    }
}

internal sealed record AddItemRequest(Guid StoreProductId, int? Quantity);

internal sealed record SetQuantityRequest(int Quantity);

internal sealed record CartResponse(List<CartLineResponse> Items, int Count, decimal ItemsTotal, decimal VatTotal, bool Changed)
{
    public static readonly CartResponse Empty = new([], 0, 0m, 0m, false);

    public static CartResponse From(CartContents contents) => new(
        [
            .. contents.Items.Select(item => new CartLineResponse(
                item.Product.StoreProductId,
                item.Product.Name,
                item.Product.Slug,
                item.Product.Price,
                item.Quantity,
                item.LineTotal,
                item.Available,
                item.Product.ImageUrl)),
        ],
        contents.Count,
        contents.ItemsTotal,
        contents.VatTotal,
        contents.Changed);
}

internal sealed record CartLineResponse(
    Guid StoreProductId,
    string Name,
    string Slug,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    int Available,
    string? ImageUrl);
