using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Discounts;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Storefront;

internal sealed class Carts(
    HttpContext httpContext,
    DbContext dbContext,
    IStoreContext storeContext,
    ISellableProducts products,
    IStockLedger stock,
    DiscountCodes discounts,
    TimeProvider clock)
{
    private const string CookieName = "shopforge_cart";

    public async Task<Cart> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var cart = await FindAsync(cancellationToken);

        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart(storeContext.StoreId!.Value, clock.GetUtcNow());
        dbContext.Add(cart);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The cookie only carries the cart id; the store's own filter makes it useless on another store.
        httpContext.Response.Cookies.Append(CookieName, cart.Id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(30),
            Path = "/",
        });

        return cart;
    }

    public async Task<Cart?> FindAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.Request.Cookies[CookieName], out var cartId))
        {
            return null;
        }

        return await dbContext.Set<Cart>().SingleOrDefaultAsync(cart => cart.Id == cartId, cancellationToken);
    }

    public void Forget() => httpContext.Response.Cookies.Delete(CookieName);

    public void Touch(Cart cart) => cart.Touch(clock.GetUtcNow());

    // Lines are priced from the catalog on every read: products that disappeared drop out of the cart, and quantities
    // are capped at what is in stock so checkout is not the first place a customer hears about it.
    public async Task<CartContents> ContentsAsync(Cart cart, CancellationToken cancellationToken)
    {
        var sellable = await products.FindAsync([.. cart.Lines.Select(line => line.StoreProductId)], cancellationToken);
        var available = await stock.AvailableAsync([.. cart.Lines.Select(line => line.VariantId)], cancellationToken);

        var changed = false;
        var shortNames = new List<string>();

        foreach (var line in cart.Lines.ToList())
        {
            var product = sellable.SingleOrDefault(candidate => candidate.StoreProductId == line.StoreProductId);
            // A form that is no longer part of the product has nothing left in stock, whatever the shelf says.
            var limit = product?.Form(line.VariantId) is null ? 0 : available.GetValueOrDefault(line.VariantId);

            if (line.Quantity <= limit)
            {
                continue;
            }

            cart.SetQuantity(line.StoreProductId, line.VariantId, limit);
            changed = true;

            if (product is not null)
            {
                shortNames.Add(product.Name);
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var items = cart.Lines
            .Select(line => new
            {
                Line = line,
                Product = sellable.SingleOrDefault(candidate => candidate.StoreProductId == line.StoreProductId),
            })
            .Where(pair => pair.Product?.Form(pair.Line.VariantId) is not null)
            .Select(pair => new CartItem(
                pair.Product!,
                pair.Product!.Form(pair.Line.VariantId)!,
                pair.Line.Quantity,
                available.GetValueOrDefault(pair.Line.VariantId)))
            .OrderBy(item => item.Product.Name)
            .ThenBy(item => item.Variant.Position)
            .ToList();

        var (discount, problem) = await DiscountForAsync(cart, items, cancellationToken);

        return new CartContents(items, changed, shortNames, discount, problem);
    }

    // A code can stop applying while it sits in the cart — it expires, it is used up, the cart drops below its
    // minimum. The cart keeps it and reports why, because an order must not quietly cost more than it showed.
    private async Task<(AppliedDiscount? Applied, DiscountProblem? Problem)> DiscountForAsync(
        Cart cart,
        IReadOnlyList<CartItem> items,
        CancellationToken cancellationToken)
    {
        if (cart.DiscountCode is not { Length: > 0 } code || items.Count == 0)
        {
            return (null, null);
        }

        var discount = await discounts.FindAsync(code, cancellationToken);

        if (discount is null)
        {
            return (null, DiscountProblem.Unknown);
        }

        if (await discounts.FindProblemAsync(discount, items.Sum(item => item.LineTotal), email: null, cancellationToken) is { } problem)
        {
            return (null, problem);
        }

        var allocation = DiscountAllocation.For(discount, [.. items.Select(item => item.LineTotal)], shippingPrice: 0m);

        return (new AppliedDiscount(discount, allocation), null);
    }
}

internal sealed record CartItem(SellableProduct Product, SellableVariant Variant, int Quantity, int Available)
{
    public decimal LineTotal => Product.Price * Quantity;
}

internal sealed record AppliedDiscount(Discount Discount, DiscountResult Result);

internal sealed record CartContents(
    IReadOnlyList<CartItem> Items,
    bool Changed,
    IReadOnlyList<string> ShortNames,
    AppliedDiscount? Discount = null,
    DiscountProblem? DiscountProblem = null)
{
    public decimal ItemsTotal => Items.Sum(item => item.LineTotal) - DiscountTotal;

    public decimal DiscountTotal => Discount?.Result.LineDiscounts.Sum() ?? 0m;

    public decimal VatTotal => Items
        .Select((item, index) => Money.VatOf(item.LineTotal - LineDiscount(index), item.Product.VatRate))
        .Sum();

    public int Count => Items.Sum(item => item.Quantity);

    public decimal LineDiscount(int index) => Discount?.Result.LineDiscounts[index] ?? 0m;
}
