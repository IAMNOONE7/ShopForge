using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Discounts;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Persistence;
using ShopForge.Orders.Returns;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;
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
        storefront.MapPost("/checkout", PlaceOrderAsync).Idempotent();
        storefront.MapGet("/orders/{number}", GetOrderAsync);
    }

    private static async Task<Results<Ok<CheckoutMethodsResponse>, ValidationProblem>> GetMethodsAsync(
        string? country,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ISellableProducts products,
        IStockLedger stock,
        ICurrentStoreSettings storeSettings,
        ITaxRates taxRates,
        IEnumerable<IPaymentProvider> paymentProviders,
        IProviderConnections connections,
        IEnumerable<IShippingProvider> shippingProviders,
        DiscountCodes discounts,
        ShippingQuotes quotes,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Where the parcel is going, as checkout will decide it: the delivery address's country, or the billing
        // address's when there is no separate delivery address — for a pickup point as much as for a door, because
        // that is the country checkout holds the method to. Without it the shopper has not said yet, and a method
        // limited by destination stays on offer and is refused at checkout if the address turns out not to fit
        // (D-145, D-204).
        var destination = country?.Trim();

        if (destination is { Length: > 0 } && !(destination.Length == 2 && destination.All(char.IsAsciiLetter)))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["country"] = ["A country is its two-letter code, such as CZ."] });
        }

        destination = destination is { Length: > 0 } ? destination.ToUpperInvariant() : null;

        // A method whose provider is not configured on this deployment is not on offer, however active the store left it.
        var providerKeys = await UsableProvidersAsync(paymentProviders, connections, cancellationToken);
        var payment = await dbContext.Set<PaymentMethod>()
            .Where(method => method.IsActive && providerKeys.Contains(method.ProviderKey))
            .OrderBy(method => method.Name)
            .Select(method => new PaymentMethodResponse(method.Code, method.Name))
            .ToListAsync(cancellationToken);

        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, discounts, storeSettings, taxRates, clock);
        var cart = await carts.FindAsync(cancellationToken);
        var contents = cart is null ? null : await carts.ContentsAsync(cart, cancellationToken);
        IReadOnlyList<QuotedItem> items = contents is null ? [] : Quoted(contents);
        var goods = contents?.ItemsTotal ?? 0m;
        var carriers = await UsableProvidersAsync(shippingProviders, connections, cancellationToken);
        var methods = await dbContext.Set<ShippingMethod>()
            .AsNoTracking()
            .Where(method => method.IsActive && method.RatesFrom != RateSource.Placeholder && carriers.Contains(method.ProviderKey))
            .OrderBy(method => method.Price)
            .ThenBy(method => method.Name)
            .ToListAsync(cancellationToken);
        var shipping = new List<ShippingMethodResponse>();
        var unavailable = new List<UnavailableShippingResponse>();

        // Each method answers with the quote the checkout will charge (D-220). One that cannot take this cart is listed
        // apart with a reason a shopper can act on; one whose trouble is the store's own configuration is left out, as a
        // method with no account behind it is, and so is one still on the invented placeholder prices (D-219), whatever
        // readiness said when the store opened.
        foreach (var method in methods)
        {
            // An empty cart is nothing to estimate parcels for.
            if (method.Pricing == ShippingPricing.PerParcel && items.Count == 0)
            {
                continue;
            }

            var quote = await quotes.QuoteAsync(method, items, destination, goods, cancellationToken);

            if (quote.Unavailable is { } refusal)
            {
                // Nothing in an empty cart is the shopper's to change, so it lists no method apart.
                if (items.Count > 0 && ShopperReason(refusal.Reason) is { } reason)
                {
                    unavailable.Add(new UnavailableShippingResponse(method.Code, method.Name, reason, refusal.VariantId));
                }

                continue;
            }

            shipping.Add(new ShippingMethodResponse(
                method.Code,
                method.Name,
                quote.Charged,
                method.RequiresPickupPoint,
                Choice(method, shippingProviders),
                method.Pricing,
                method.Pricing == ShippingPricing.PerParcel ? quote.Parcels.Count : null,
                MapParcel(ShippingQuotes.ForPoint(quote, items, destination)),
                quote.Total));
        }

        // Without a cart there is no cart's settings read to borrow the threshold from, so it is asked for here.
        var threshold = contents is null ? (await storeSettings.GetAsync(cancellationToken)).FreeShippingThreshold : contents.FreeShippingThreshold;

        // Cheapest first, as before; a method priced by the parcel takes its place by what this cart costs.
        return TypedResults.Ok(new CheckoutMethodsResponse(
            payment,
            [.. shipping.OrderBy(method => method.Price)],
            destination,
            unavailable,
            FreeShippingResponse.For(threshold, goods)));
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

        // A carrier whose points are chosen in its own map has no list to give, and asking for one would be
        // either a call for nothing or every box in the country. The shopper picks in the widget instead.
        var points = provider.PickupPoints == PickupPointChoice.InTheCarriersMap
            ? []
            : await provider.FindPickupPointsAsync(cancellationToken);

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
        ITaxRates taxRates,
        ICurrentCustomer currentCustomer,
        IStockLedger stock,
        IEnumerable<IPaymentProvider> paymentProviders,
        IProviderConnections connections,
        IEnumerable<IShippingProvider> shippingProviders,
        DiscountCodes discounts,
        ShippingQuotes quotes,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        ILoggerFactory loggerFactory,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var carts = new Carts(httpContext, dbContext, storeContext, products, stock, discounts, storeSettings, taxRates, clock);
        var cart = await carts.FindAsync(cancellationToken);
        var contents = cart is null ? null : await carts.ContentsAsync(cart, cancellationToken);

        var providerKeys = await UsableProvidersAsync(paymentProviders, connections, cancellationToken);
        var payment = await dbContext.Set<PaymentMethod>()
            .SingleOrDefaultAsync(
                method => method.Code == request.PaymentMethodCode && method.IsActive && providerKeys.Contains(method.ProviderKey),
                cancellationToken);
        var shippingProviderKeys = await UsableProvidersAsync(shippingProviders, connections, cancellationToken);
        var shipping = await dbContext.Set<ShippingMethod>()
            .SingleOrDefaultAsync(
                method => method.Code == request.ShippingMethodCode
                    && method.IsActive
                    && method.RatesFrom != RateSource.Placeholder
                    && shippingProviderKeys.Contains(method.ProviderKey),
                cancellationToken);
        var carrier = shipping is null
            ? null
            : shippingProviders.SingleOrDefault(candidate => candidate.Key == shipping.ProviderKey);

        // The delivery is priced by the same quote the shopper was shown (D-220), from the rows this order is written with:
        // the method read above and the cart as it stands now.
        var destinationCountry = (request.ShippingAddress ?? request.BillingAddress)?.Country?.Trim().ToUpperInvariant();
        IReadOnlyList<QuotedItem> items = contents is null ? [] : Quoted(contents);
        var quote = shipping is null || items.Count == 0 ? null : await quotes.QuoteAsync(shipping, items, destinationCountry, contents!.ItemsTotal, cancellationToken);
        var chosenPoint = carrier is not null && quote is { IsAvailable: true } && shipping!.RequiresPickupPoint && !string.IsNullOrWhiteSpace(request.PickupPointCode)
            ? await AskAboutPointAsync(carrier, request.PickupPointCode, ShippingQuotes.ForPoint(quote, items, destinationCountry), loggerFactory, cancellationToken)
            : new PickupPointAnswer(null, Answered: true);

        // A carrier that cannot be reached has not refused the point; it has said nothing. An order placed on
        // that silence would be one nobody has checked, so the shopper is asked to try again instead (D-160).
        if (!chosenPoint.Answered)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The pickup point could not be checked",
                detail: "The carrier did not answer. Nothing has been ordered; please try again in a moment.");
        }

        var pickupPoint = chosenPoint.Point;

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

        // A code that stopped applying is a change to what the shopper agreed to pay, so the order stops here.
        if (contents?.DiscountProblem is { } discountProblem)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The discount code cannot be used",
                detail: DiscountCodes.Explain(discountProblem));
        }

        // An order of a signed-in customer is confirmed to the address the account was proved with, whatever the
        // form sent: the shop must not be usable to write to an address its owner never gave it (D-101).
        var account = await currentCustomer.FindAsync(cancellationToken);
        var email = account?.Email ?? request.Email;

        var errors = new RequestErrors()
            .Check(IsEmail(email), "email", "A valid e-mail address is required.")
            .Check(IsPhone(request.Phone), "phone", "A telephone number is required so the carrier can reach the recipient.")
            .Check(request.BillingAddress?.IsComplete == true, "billingAddress", "The billing address is incomplete.")
            .Check(request.ShippingAddress is null || request.ShippingAddress.IsComplete, "shippingAddress", "The shipping address is incomplete.")
            .Check(payment is not null, "paymentMethodCode", "Choose one of the store's payment methods.")
            .Check(shipping is not null, "shippingMethodCode", "Choose one of the store's shipping methods.")
            .Check(
                quote?.Unavailable is not { } refusal,
                "shippingMethodCode",
                quote?.Unavailable is { } unavailable ? Explain(unavailable.Reason) : string.Empty)
            .Check(quote?.Unavailable is not null || shipping?.RequiresPickupPoint != true || pickupPoint is not null, "pickupPointCode", "Choose one of the pickup points.")
            // A price by the parcel depends on the cart and on prices the store can change at any moment, so the shopper
            // says which price they agreed to. A flat price is checked when it is sent (D-220).
            .Check(
                shipping?.Pricing != ShippingPricing.PerParcel || request.ExpectedShippingPrice is not null,
                "expectedShippingPrice",
                "Say which delivery price you were shown.")
            .Check(contents is { Items.Count: > 0 }, "cart", "The cart is empty.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        // Never a quietly different price: one that moved since the page showed it is refused with the new quote. The
        // shopper was shown what they pay, so that is what is compared: nothing, when the goods reached the free-delivery
        // threshold (D-221).
        if (request.ExpectedShippingPrice is { } expected && expected != quote!.Charged)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The delivery price has changed",
                detail: $"Delivery now costs {quote.Charged} {quote.Currency}. Please check it before ordering again.",
                extensions: new Dictionary<string, object?> { ["shippingQuote"] = quote });
        }

        var settings = await storeSettings.GetAsync(cancellationToken);

        // Where the parcel is going decides what everything on it attracts, so the rates are read once the
        // address is known and before a single line is written (D-187).
        var destination = (request.ShippingAddress ?? request.BillingAddress)!.Country;
        var rates = await taxRates.ForAsync(destination, settings.Seller?.Country, cancellationToken);
        var placedAt = clock.GetUtcNow();
        var reservationExpiresAt = placedAt + ReservationWindow;

        // Number, stock and order rows are written together: a checkout that cannot reserve leaves nothing behind.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var number = await Numbers.NextOrderNumberAsync(dbContext, storeContext.StoreId!.Value, placedAt.Year, cancellationToken);
        var requests = contents!.Items
            .GroupBy(item => item.Variant.Id)
            .Select(group => new StockRequest(group.Key, group.Sum(item => item.Quantity)))
            .ToList();
        var reserved = await stock.ReserveAsync(requests, number, reservationExpiresAt, cancellationToken);

        if (!reserved.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            metrics.ReservationRefused();

            var names = contents.Items
                .Where(item => reserved.UnavailableVariantIds.Contains(item.Variant.Id))
                .Select(item => Named(item));

            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Some products are no longer in stock",
                detail: $"{string.Join(", ", names)}: not enough items left. Please check your cart.");
        }

        // The code is re-checked and counted here, not in the cart: only one checkout can take the last redemption
        // (D-086), and a code that ran out while the cart sat open must not be honoured.
        var discount = contents.Discount?.Discount;
        var allocation = discount is null
            ? null
            : DiscountAllocation.For(discount, [.. contents.Items.Select(item => item.LineTotal)], quote!.Total, contents.Currency);

        if (discount is not null)
        {
            var refused = await discounts.FindProblemAsync(discount, contents.Items.Sum(item => item.LineTotal), settings.FreeShippingThreshold, email, cancellationToken);

            if (refused is not null || !await discounts.TryRedeemAsync(discount, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);

                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The discount code cannot be used",
                    detail: DiscountCodes.Explain(refused ?? DiscountProblem.UsedUp));
            }
        }

        var order = new Order(
            storeContext.StoreId!.Value,
            number,
            settings.Currency,
            email!,
            request.Phone!,
            request.BillingAddress!.ToAddress(),
            (request.ShippingAddress ?? request.BillingAddress).ToAddress(),
            new ChosenMethods(
                payment!.Code,
                payment.Name,
                payment.ProviderKey,
                shipping!.ProviderKey,
                shipping.Code,
                shipping.Name,
                quote!.Total,
                rates.For(shipping.TaxClass, shipping.VatRate)),
            pickupPoint is null
                ? null
                : new ChosenPickupPoint(
                    pickupPoint.Code,
                    pickupPoint.Name,
                    new Address(pickupPoint.Name, pickupPoint.Line1, null, pickupPoint.City, pickupPoint.PostalCode, pickupPoint.Country)),
            placedAt,
            reservationExpiresAt);

        if (account is not null)
        {
            order.AssignTo(account.StoreCustomerId);
        }

        dbContext.AddRange(quote.Parcels.Select((parcel, index) => new OrderParcel(
            order.StoreId,
            order.Number,
            index + 1,
            parcel.GrossWeightGrams,
            parcel.BandUpToGrams,
            parcel.Price,
            parcel.Alone,
            parcel.Contents.Select(content => new OrderParcelItem(content.VariantId, content.Sku, content.Quantity)))));

        foreach (var (item, index) in contents.Items.Select((item, index) => (item, index)))
        {
            order.AddLine(
                item.Product.StoreProductId,
                item.Variant.Id,
                Named(item),
                item.Product.Price,
                rates.For(item.Product.TaxClass, item.Product.VatRate),
                item.Quantity,
                allocation?.LineDiscounts[index] ?? 0m);
        }

        if (discount is not null && allocation is not null)
        {
            order.ApplyDiscount(discount.Code, discount.Name, allocation.ShippingDiscount);
            dbContext.Add(new DiscountRedemption(
                order.StoreId,
                discount.Id,
                order.Number,
                order.Email,
                allocation.Total,
                placedAt));
        }

        if (quote.FreeShippingThreshold is { } freeFrom)
        {
            order.MakeDeliveryFree(freeFrom);
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
                order.ReservationExpiresAt,
                order.BillingAddress.FullName,
                order.Phone!,
                order.BillingAddress.Country,
                Language(settings.Culture),
                // Which it is, is the order's own fact: a point was chosen or it was not. The method only says
                // whether one had to be.
                pickupPoint is null ? PaymentDelivery.ToAddress : PaymentDelivery.ToPickupPoint),
            loggerFactory,
            cancellationToken);

        // What was asked of the provider, written with the order: a shopper who comes back and tries again makes
        // a second attempt rather than erasing the first one (D-141).
        dbContext.Add(new PaymentAttempt(
            order.StoreId,
            order.Number,
            payment.ProviderKey,
            instructions.Reference,
            order.GrandTotal,
            order.Currency,
            instructions.RedirectUrl,
            placedAt));

        // The event goes in with the order, so a confirmation is never sent for an order that was rolled back (D-065).
        outbox.Enqueue(new OrderPlaced(order.Number, order.Email, order.GrandTotal, order.Currency.Code, instructions.Message));
        metrics.OrderPlaced(payment.Code);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        carts.Forget();

        return TypedResults.Created(
            $"/api/storefront/orders/{order.Number}?token={order.AccessToken}",
            new PlacedOrderResponse(order.Number, order.AccessToken, instructions.Message, instructions.RedirectUrl));
    }

    // A gateway a merchant signed up for is only on offer where that merchant has connected it: a store that has
    // not finished connecting one is not shown its methods rather than being shown a method that cannot take money
    // (D-138).
    // The same question for a gateway and a carrier, asked once: which of these can this store actually use
    // today. A platform account is not a store's to use unless the deployment says so (D-196); a provider that
    // needs no connection is otherwise always usable; one that does is usable where the store has finished
    // connecting an account to it (D-138, D-153).
    internal static async Task<List<string>> UsableProvidersAsync<TProvider>(
        IEnumerable<TProvider> providers,
        IProviderConnections connections,
        CancellationToken cancellationToken)
        where TProvider : IConnectedProvider
    {
        var all = providers.Where(provider => provider.IsOfferedToStores).ToList();

        if (!all.Any(provider => provider.NeedsConnection))
        {
            return [.. all.Select(provider => provider.Key)];
        }

        var connected = await connections.ConnectedAsync(cancellationToken);

        return [.. all.Where(provider => !provider.NeedsConnection || connected.Contains(provider.Key)).Select(provider => provider.Key)];
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

        return order is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(OrderResponse.From(
                order,
                await Documents.OfAsync(dbContext, order.Number, cancellationToken),
                await CustomerRefunds.OfOrderAsync(dbContext, order.Number, cancellationToken)));
    }

    // The store's own language, which is the only one ShopForge has: a shopper reading the storefront in
    // another is Stage 32's problem, and guessing from a header would be worse than saying what the shop is.
    private static string Language(string culture) =>
        culture.Split('-') is [{ Length: > 0 } language, ..] ? language.ToLowerInvariant() : culture;

    // "Oak Chair" on its own, "Linen Shirt (L)" when the shop sells it in more than one form. The order line
    // keeps this as written, so the invoice, the credit note and the return all say which one was bought even
    // after the product or the size is renamed (D-136).
    private static string Named(CartItem item)
    {
        var named = item.Variant.OptionValues.Count == 0
            ? item.Product.Name
            : $"{item.Product.Name} ({string.Join(" / ", item.Variant.OptionValues)})";

        return named.Length <= OrderLine.MaxProductNameLength ? named : named[..OrderLine.MaxProductNameLength];
    }

    // The three words the shop knows: there is nowhere to choose, ask us for a list, or open the carrier's
    // map. A method that delivers to the door says `none` whoever carries it — the same carrier offering both
    // a box and a doorstep must not have the doorstep claim a map nobody should open (D-161).
    private static string Choice(ShippingMethod method, IEnumerable<IShippingProvider> shippingProviders) =>
        !method.RequiresPickupPoint ? "none"
        : shippingProviders.SingleOrDefault(candidate => candidate.Key == method.ProviderKey) is { PickupPoints: PickupPointChoice.InTheCarriersMap } ? "carrier-map"
        : "list";

    // "Is this one of yours" and "are you there at all" are different questions with the same shape, and the
    // checkout has to tell them apart: null is a refusal, silence is not.
    private static async Task<PickupPointAnswer> AskAboutPointAsync(
        IShippingProvider carrier,
        string code,
        Parcel parcel,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return new PickupPointAnswer(await carrier.FindPickupPointAsync(code, parcel, cancellationToken), Answered: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger(typeof(CheckoutEndpoints))
                .LogError(exception, "Asking {Carrier} about a chosen pickup point failed.", carrier.Key);

            return new PickupPointAnswer(null, Answered: false);
        }
    }

    private static List<QuotedItem> Quoted(CartContents contents) => [.. contents.Items.Select(item => new QuotedItem(item.Variant, item.Quantity))];

    // What a shopper can do something about, in five words; a method the store has misconfigured is no business of
    // theirs and is left out instead (D-220).
    private static string? ShopperReason(QuoteRefusalReason reason) => reason switch
    {
        QuoteRefusalReason.NotServed => "notToCountry",
        QuoteRefusalReason.TooHeavy or QuoteRefusalReason.ItemTooHeavy or QuoteRefusalReason.AboveHighestBand => "tooHeavy",
        QuoteRefusalReason.ItemTooLarge => "tooLarge",
        QuoteRefusalReason.MissingWeight or QuoteRefusalReason.MissingSize => "missingData",
        QuoteRefusalReason.TooManyParcels => "tooManyParcels",
        _ => null,
    };

    private static string Explain(QuoteRefusalReason reason) => ShopperReason(reason) switch
    {
        "notToCountry" => "That delivery method does not deliver to this country.",
        "tooHeavy" => "This order is too heavy for that delivery method.",
        "tooLarge" => "Something in this order is too large for that delivery method.",
        "missingData" => "That delivery method cannot be priced for something in this order.",
        "tooManyParcels" => "This order is too large to send by parcel. Please contact the store about delivering it.",
        _ => "That delivery method cannot carry this order.",
    };

    // The parcel the carrier's map is opened for, in the map's own units: kilograms, and sides rounded up to whole
    // centimetres (D-220). Only a method priced by the parcel has one.
    private static MapParcelResponse? MapParcel(Parcel parcel) =>
        parcel.WeightGrams is { } grams
            ? new MapParcelResponse(grams / 1000m, Parcel.Centimetres(parcel.LongestMm), Parcel.Centimetres(parcel.MiddleMm), Parcel.Centimetres(parcel.ShortestMm))
            : null;

    // Enough to tell a telephone number from a line of prose, and no more: what a carrier will accept is the
    // carrier's rule to state, not ours to guess at (D-144).
    private static bool IsPhone(string? phone) =>
        phone is { Length: <= 30 } && phone.Count(char.IsAsciiDigit) >= 6
        && phone.All(character => char.IsAsciiDigit(character) || character is ' ' or '+' or '-' or '(' or ')' or '/');

    private static bool IsEmail(string? email) =>
        email is { Length: <= 254 } && email.Count(character => character == '@') == 1 && email.Trim().Length == email.Length
        && email.Split('@') is [{ Length: > 0 }, { Length: > 2 } domain] && domain.Contains('.');
}

// What the carrier said about the point the shopper chose, and whether it said anything at all.
internal sealed record PickupPointAnswer(PickupPoint? Point, bool Answered);

internal sealed record CheckoutRequest(
    string? Email,
    string? Phone,
    AddressRequest? BillingAddress,
    AddressRequest? ShippingAddress,
    string? PaymentMethodCode,
    string? ShippingMethodCode,
    string? PickupPointCode,
    decimal? ExpectedShippingPrice = null);

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

// The country the methods were chosen for, so a page can tell an answer to an address the shopper has since changed
// from one to the address in front of them.
internal sealed record CheckoutMethodsResponse(
    List<PaymentMethodResponse> PaymentMethods,
    List<ShippingMethodResponse> ShippingMethods,
    string? DestinationCountry,
    List<UnavailableShippingResponse> UnavailableShippingMethods,
    FreeShippingResponse? FreeShipping);

internal sealed record PaymentMethodResponse(string Code, string Name);

// `PickupPointChoice` tells the shop which way to ask: nowhere, our own list of points, or the carrier's map
// opened in the shopper's browser. Without it an empty list of points is indistinguishable from a store that
// configured none (D-158).
// `Price` is what the shopper pays for this cart, the flat price for a flat method; `Parcels` is how many parcels a method
// priced by the parcel estimates, and `MapParcel` what the carrier's map is told about the heaviest of them (D-220).
// `FullPrice` is the delivery's price before free delivery took it off, and `Price` when it did not (D-221).
internal sealed record ShippingMethodResponse(
    string Code,
    string Name,
    decimal Price,
    bool RequiresPickupPoint,
    string PickupPointChoice,
    ShippingPricing Pricing,
    int? Parcels,
    MapParcelResponse? MapParcel,
    decimal FullPrice);

// A method that cannot take this cart, and why: notToCountry, tooHeavy, tooLarge, missingData or tooManyParcels, and the
// form to blame where there is one.
internal sealed record UnavailableShippingResponse(string Code, string Name, string Reason, Guid? VariantId);

internal sealed record MapParcelResponse(decimal WeightKg, int? LengthCm, int? WidthCm, int? DepthCm);


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
    OrderDiscountResponse? Discount,
    string? PickupPoint,
    ShipmentResponse? Shipment,
    List<DocumentResponse> Documents,
    List<OrderLineResponse> Lines,
    // When the payment was confirmed, which a status of Paid or later only implies, and where any money given back
    // stands (D-204).
    DateTimeOffset? PaidAt,
    List<CustomerRefundResponse> Refunds,
    // The threshold the goods reached when that is what made delivery free (D-221).
    decimal? FreeShippingThreshold)
{
    public static OrderResponse From(Order order, List<DocumentResponse> documents, List<CustomerRefundResponse> refunds) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.Currency.Code,
        order.PaymentMethodName,
        order.ShippingMethodName,
        order.ShippingCharged,
        order.ItemsTotal,
        order.VatTotal,
        order.GrandTotal,
        order.DiscountCode is null ? null : new OrderDiscountResponse(order.DiscountCode, order.DiscountName!, order.DiscountTotal),
        order.PickupPointName is null ? null : $"{order.PickupPointName}, {order.PickupPointAddress!.Line1}, {order.PickupPointAddress.City}",
        order.Shipment is null ? null : new ShipmentResponse(order.Shipment.Carrier, order.Shipment.TrackingNumber, order.Shipment.TrackingUrl),
        documents,
        [.. order.Lines.Select(line => new OrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))],
        order.PaidAt,
        refunds,
        order.FreeShippingThreshold);
}

internal sealed record ShipmentResponse(string Carrier, string TrackingNumber, string? TrackingUrl);

internal sealed record OrderDiscountResponse(string Code, string Name, decimal Amount);

internal sealed record OrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);
