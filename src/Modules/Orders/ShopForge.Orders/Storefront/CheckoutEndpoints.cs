using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Persistence;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Storefront;

internal static class CheckoutEndpoints
{
    // How long an unpaid order keeps its stock before the sweep gives it back (D-048). Hosted checkout sessions must
    // live at least half an hour, so the window is a little longer than that and the session expires with it (D-056).
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(35);

    public static void MapCheckout(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/checkout/methods", GetMethodsAsync);
        storefront.MapPost("/checkout", PlaceOrderAsync);
        storefront.MapGet("/orders/{number}", GetOrderAsync);
    }

    private static async Task<Ok<CheckoutMethodsResponse>> GetMethodsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<PaymentMethod>()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Name)
            .Select(method => new PaymentMethodResponse(method.Code, method.Name))
            .ToListAsync(cancellationToken);

        var shipping = await dbContext.Set<ShippingMethod>()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Price)
            .ThenBy(method => method.Name)
            .Select(method => new ShippingMethodResponse(method.Code, method.Name, method.Price))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CheckoutMethodsResponse(payment, shipping));
    }

    private static async Task<Results<Created<PlacedOrderResponse>, ValidationProblem, ProblemHttpResult>> PlaceOrderAsync(
        CheckoutRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        ICurrentStoreSettings storeSettings,
        ICurrentCustomer currentCustomer,
        IStockLedger stock,
        IEnumerable<IPaymentProvider> paymentProviders,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var carts = new Carts(httpContext, dbContext, storeContext, products, stock);
        var cart = await carts.FindAsync(cancellationToken);
        var contents = cart is null ? null : await carts.ContentsAsync(cart, cancellationToken);

        var payment = await dbContext.Set<PaymentMethod>()
            .SingleOrDefaultAsync(method => method.Code == request.PaymentMethodCode && method.IsActive, cancellationToken);
        var shipping = await dbContext.Set<ShippingMethod>()
            .SingleOrDefaultAsync(method => method.Code == request.ShippingMethodCode && method.IsActive, cancellationToken);

        if (contents is { Changed: true })
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Some products are no longer available",
                detail: contents.ShortNames.Count > 0
                    ? $"{string.Join(", ", contents.ShortNames)}: fewer items are in stock than your cart held. Please check it before ordering again."
                    : "The cart was updated; please check it before ordering again.");
        }

        var errors = new RequestErrors()
            .Check(IsEmail(request.Email), "email", "A valid e-mail address is required.")
            .Check(request.BillingAddress?.IsComplete == true, "billingAddress", "The billing address is incomplete.")
            .Check(request.ShippingAddress is null || request.ShippingAddress.IsComplete, "shippingAddress", "The shipping address is incomplete.")
            .Check(payment is not null, "paymentMethodCode", "Choose one of the store's payment methods.")
            .Check(shipping is not null, "shippingMethodCode", "Choose one of the store's shipping methods.")
            .Check(contents is { Items.Count: > 0 }, "cart", "The cart is empty.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var settings = await storeSettings.GetAsync(cancellationToken);
        var placedAt = clock.GetUtcNow();
        var reservationExpiresAt = placedAt + ReservationWindow;

        // Number, stock and order rows are written together: a checkout that cannot reserve leaves nothing behind.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var number = await OrderNumbers.NextAsync(dbContext, storeContext.StoreId!.Value, placedAt.Year, cancellationToken);
        var requests = contents!.Items
            .GroupBy(item => item.Product.ProductId)
            .Select(group => new StockRequest(group.Key, group.Sum(item => item.Quantity)))
            .ToList();
        var reserved = await stock.ReserveAsync(requests, number, reservationExpiresAt, cancellationToken);

        if (!reserved.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            var names = contents.Items
                .Where(item => reserved.UnavailableProductIds.Contains(item.Product.ProductId))
                .Select(item => item.Product.Name);

            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Some products are no longer in stock",
                detail: $"{string.Join(", ", names)}: not enough items left. Please check your cart.");
        }

        var order = new Order(
            storeContext.StoreId!.Value,
            number,
            settings.Currency,
            request.Email!,
            request.BillingAddress!.ToAddress(),
            (request.ShippingAddress ?? request.BillingAddress).ToAddress(),
            new ChosenMethods(payment!.Code, payment.Name, shipping!.Code, shipping.Name, shipping.Price, shipping.VatRate),
            placedAt,
            reservationExpiresAt);

        if (await currentCustomer.FindStoreCustomerIdAsync(cancellationToken) is { } storeCustomerId)
        {
            order.AssignTo(storeCustomerId);
        }

        foreach (var item in contents.Items)
        {
            order.AddLine(item.Product.StoreProductId, item.Product.Name, item.Product.Price, item.Product.VatRate, item.Quantity);
        }

        dbContext.Add(order);
        dbContext.Remove(cart!);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        carts.Forget();

        var provider = paymentProviders.Single(candidate => candidate.Key == payment.ProviderKey);
        var storefront = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var instructions = await provider.StartAsync(
            new PaymentRequest(
                order.StoreId,
                order.Number,
                order.GrandTotal,
                order.Currency,
                order.Email,
                ReturnUrl: $"{storefront}/order/{order.Number}?token={order.AccessToken}",
                CancelUrl: $"{storefront}/cart",
                order.ReservationExpiresAt),
            cancellationToken);

        return TypedResults.Created(
            $"/api/storefront/orders/{order.Number}?token={order.AccessToken}",
            new PlacedOrderResponse(order.Number, order.AccessToken, instructions.Message, instructions.RedirectUrl));
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrderAsync(
        string number,
        Guid token,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Number == number && order.AccessToken == token, cancellationToken);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(OrderResponse.From(order));
    }

    private static bool IsEmail(string? email) =>
        email is { Length: <= 254 } && email.Count(character => character == '@') == 1 && email.Trim().Length == email.Length
        && email.Split('@') is [{ Length: > 0 }, { Length: > 2 } domain] && domain.Contains('.');
}

internal sealed record CheckoutRequest(
    string? Email,
    AddressRequest? BillingAddress,
    AddressRequest? ShippingAddress,
    string? PaymentMethodCode,
    string? ShippingMethodCode);

internal sealed record AddressRequest(string? FullName, string? Line1, string? Line2, string? City, string? PostalCode, string? Country)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(FullName)
        && !string.IsNullOrWhiteSpace(Line1)
        && !string.IsNullOrWhiteSpace(City)
        && !string.IsNullOrWhiteSpace(PostalCode)
        && Country?.Trim().Length == 2;

    public Address ToAddress() => new(FullName!, Line1!, Line2, City!, PostalCode!, Country!);
}

internal sealed record CheckoutMethodsResponse(List<PaymentMethodResponse> PaymentMethods, List<ShippingMethodResponse> ShippingMethods);

internal sealed record PaymentMethodResponse(string Code, string Name);

internal sealed record ShippingMethodResponse(string Code, string Name, decimal Price);

internal sealed record PlacedOrderResponse(string Number, Guid Token, string PaymentInstructions, string? RedirectUrl);

internal sealed record OrderResponse(
    string Number,
    DateTimeOffset PlacedAt,
    string Status,
    string Email,
    string Currency,
    string PaymentMethod,
    string ShippingMethod,
    decimal ShippingPrice,
    decimal ItemsTotal,
    decimal VatTotal,
    decimal GrandTotal,
    List<OrderLineResponse> Lines)
{
    public static OrderResponse From(Order order) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.Currency,
        order.PaymentMethodName,
        order.ShippingMethodName,
        order.ShippingPrice,
        order.ItemsTotal,
        order.VatTotal,
        order.GrandTotal,
        [.. order.Lines.Select(line => new OrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))]);
}

internal sealed record OrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);
