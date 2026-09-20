using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Storefront;

internal sealed class Carts(HttpContext httpContext, DbContext dbContext, IStoreContext storeContext, ISellableProducts products)
{
    private const string CookieName = "shopforge_cart";

    public async Task<Cart> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var cart = await FindAsync(cancellationToken);

        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart(storeContext.StoreId!.Value);
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

    // Lines are priced from the catalog on every read; products that disappeared drop out of the cart.
    public async Task<CartContents> ContentsAsync(Cart cart, CancellationToken cancellationToken)
    {
        var sellable = await products.FindAsync([.. cart.Lines.Select(line => line.StoreProductId)], cancellationToken);
        var removed = cart.Lines.Where(line => sellable.All(product => product.StoreProductId != line.StoreProductId)).ToList();

        foreach (var line in removed)
        {
            cart.SetQuantity(line.StoreProductId, 0);
        }

        if (removed.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var items = sellable
            .Select(product => new CartItem(product, cart.Lines.Single(line => line.StoreProductId == product.StoreProductId).Quantity))
            .OrderBy(item => item.Product.Name)
            .ToList();

        return new CartContents(items, removed.Count);
    }
}

internal sealed record CartItem(SellableProduct Product, int Quantity)
{
    public decimal LineTotal => Product.Price * Quantity;
}

internal sealed record CartContents(IReadOnlyList<CartItem> Items, int RemovedLines)
{
    public decimal ItemsTotal => Items.Sum(item => item.LineTotal);

    public decimal VatTotal => Items.Sum(item => Money.VatOf(item.LineTotal, item.Product.VatRate));

    public int Count => Items.Sum(item => item.Quantity);
}
