using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        var available = await stock.AvailableAsync([.. sellable.Select(product => product.ProductId)], cancellationToken);

        var changed = false;
        var shortNames = new List<string>();

        foreach (var line in cart.Lines.ToList())
        {
            var product = sellable.SingleOrDefault(candidate => candidate.StoreProductId == line.StoreProductId);
            var limit = product is null ? 0 : available.GetValueOrDefault(product.ProductId);

            if (line.Quantity <= limit)
            {
                continue;
            }

            cart.SetQuantity(line.StoreProductId, limit);
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

        var items = sellable
            .Where(product => cart.Lines.Any(line => line.StoreProductId == product.StoreProductId))
            .Select(product => new CartItem(
                product,
                cart.Lines.Single(line => line.StoreProductId == product.StoreProductId).Quantity,
                available.GetValueOrDefault(product.ProductId)))
            .OrderBy(item => item.Product.Name)
            .ToList();

        return new CartContents(items, changed, shortNames);
    }
}

internal sealed record CartItem(SellableProduct Product, int Quantity, int Available)
{
    public decimal LineTotal => Product.Price * Quantity;
}

internal sealed record CartContents(IReadOnlyList<CartItem> Items, bool Changed, IReadOnlyList<string> ShortNames)
{
    public decimal ItemsTotal => Items.Sum(item => item.LineTotal);

    public decimal VatTotal => Items.Sum(item => Money.VatOf(item.LineTotal, item.Product.VatRate));

    public int Count => Items.Sum(item => item.Quantity);
}
