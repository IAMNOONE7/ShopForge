using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Persistence;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Shipping;
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
        storefront.MapGet("/checkout/pickup-points/{methodCode}", GetPickupPointsAsync);
        storefront.MapPost("/checkout", PlaceOrderAsync);
        storefront.MapGet("/orders/{number}", GetOrderAsync);
    }

    private static async Task<Ok<CheckoutMethodsResponse>> GetMethodsAsync(
        DbContext dbContext,
        IEnumerable<IPaymentProvider> paymentProviders,
        CancellationToken cancellationToken)
    {
        // A method whose provider is not configured on this deployment is not on offer, however active the store left it.
        var providerKeys = paymentProviders.Select(provider => provider.Key).ToList();
        var payment = await dbContext.Set<PaymentMethod>()
            .Where(method => method.IsActive && providerKeys.Contains(method.ProviderKey))
            .OrderBy(method => method.Name)
            .Select(method => new PaymentMethodResponse(method.Code, method.Name))
            .ToListAsync(cancellationToken);

        var shipping = await dbContext.Set<ShippingMethod>()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Price)
            .ThenBy(method => method.Name)
            .Select(method => new ShippingMethodResponse(method.Code, method.Name, method.Price, method.RequiresPickupPoint))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CheckoutMethodsResponse(payment, shipping));
    }

    private static async Task<Results<Ok<List<PickupPointResponse>>, NotFound>> GetPickupPointsAsync(
        string methodCode,
        DbContext dbContext,
        IEnumerable<IShippingProvider> shippingProviders,
        CancellationToken cancellationToken)
    {
        var method = await dbContext.Set<ShippingMethod>()
            .SingleOrDefaultAsync(candidate => candidate.Code == methodCode && candidate.IsActive, cancellationToken);
        var provider = method is null ? null : shippingProviders.SingleOrDefault(candidate => candidate.Key == method.ProviderKey);

        if (method is null || provider is null)
        {
            return TypedResults.NotFound();
        }

        var points = await provider.FindPickupPointsAsync(cancellationToken);

        return TypedResults.Ok(points
            .Select(point => new PickupPointResponse(point.Code, point.Name, point.Line1, point.City, point.PostalCode, point.Country))
            .ToList());
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
        IEnumerable<IShippingProvider> shippingProviders,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        ILoggerFactory loggerFactory,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, clock);
        var cart = await carts.FindAsync(cancellationToken);
        var contents = cart is null ? null : await carts.ContentsAsync(cart, cancellationToken);

        var providerKeys = paymentProviders.Select(provider => provider.Key).ToList();
        var payment = await dbContext.Set<PaymentMethod>()
            .SingleOrDefaultAsync(
                method => method.Code == request.PaymentMethodCode && method.IsActive && providerKeys.Contains(method.ProviderKey),
                cancellationToken);
        var shippingProviderKeys = shippingProviders.Select(provider => provider.Key).ToList();
        var shipping = await dbContext.Set<ShippingMethod>()
            .SingleOrDefaultAsync(
                method => method.Code == request.ShippingMethodCode && method.IsActive && shippingProviderKeys.Contains(method.ProviderKey),
                cancellationToken);
        var pickupPoint = shipping?.RequiresPickupPoint == true && !string.IsNullOrWhiteSpace(request.PickupPointCode)
            ? await dbContext.Set<StorePickupPoint>()
                .SingleOrDefaultAsync(point => point.Code == request.PickupPointCode && point.IsActive, cancellationToken)
            : null;

        if (contents is { Changed: true })
        {
            if (contents.ShortNames.Count > 0)
            {
                // A sale lost to stock, whether the cart noticed first or the reservation did.
                metrics.ReservationRefused();
            }

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
            .Check(shipping?.RequiresPickupPoint != true || pickupPoint is not null, "pickupPointCode", "Choose one of the pickup points.")
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
            metrics.ReservationRefused();

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
            pickupPoint is null ? null : new ChosenPickupPoint(pickupPoint.Code, pickupPoint.Name, pickupPoint.Address),
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

        var provider = paymentProviders.Single(candidate => candidate.Key == payment.ProviderKey);
        var storefront = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var instructions = await StartPaymentAsync(
            provider,
            new PaymentRequest(
                order.StoreId,
                order.Number,
                order.GrandTotal,
                order.Currency,
                order.Email,
                ReturnUrl: $"{storefront}/order/{order.Number}?token={order.AccessToken}",
                CancelUrl: $"{storefront}/cart",
                order.ReservationExpiresAt),
            loggerFactory,
            cancellationToken);

        // The event goes in with the order, so a confirmation is never sent for an order that was rolled back (D-065).
        outbox.Enqueue(new OrderPlaced(order.Number, order.Email, order.GrandTotal, order.Currency, instructions.Message));
        metrics.OrderPlaced(payment.Code);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        carts.Forget();

        return TypedResults.Created(
            $"/api/storefront/orders/{order.Number}?token={order.AccessToken}",
            new PlacedOrderResponse(order.Number, order.AccessToken, instructions.Message, instructions.RedirectUrl));
    }

    // The order is committed before the provider is called: if the provider is down the shopper still has an order,
    // its stock and its link, and the store can take the payment another way (or the reservation runs out).
    private static async Task<PaymentInstructions> StartPaymentAsync(
        IPaymentProvider provider,
        PaymentRequest request,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.StartAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger(typeof(CheckoutEndpoints))
                .LogError(exception, "Starting the {Provider} payment for order {OrderNumber} failed.", provider.Key, request.OrderNumber);

            return new PaymentInstructions(
                $"Order {request.OrderNumber} is placed, but the payment could not be started. The store will contact you about paying.");
        }
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
    string? ShippingMethodCode,
    string? PickupPointCode);

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

internal sealed record ShippingMethodResponse(string Code, string Name, decimal Price, bool RequiresPickupPoint);

internal sealed record PickupPointResponse(string Code, string Name, string Line1, string City, string PostalCode, string Country);

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
    string? PickupPoint,
    ShipmentResponse? Shipment,
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
        order.PickupPointName is null ? null : $"{order.PickupPointName}, {order.PickupPointAddress!.Line1}, {order.PickupPointAddress.City}",
        order.Shipment is null ? null : new ShipmentResponse(order.Shipment.Carrier, order.Shipment.TrackingNumber, order.Shipment.TrackingUrl),
        [.. order.Lines.Select(line => new OrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))]);
}

internal sealed record ShipmentResponse(string Carrier, string TrackingNumber, string? TrackingUrl);

internal sealed record OrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);
