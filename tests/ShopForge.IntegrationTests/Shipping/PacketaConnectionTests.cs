using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using ShopForge.Infrastructure.Shipping.Packeta;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.IntegrationTests.Payments;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Shipping;

// Packeta is registered whatever the deployment holds, and means nothing until a store connects an account.
// Two credentials: one the browser is given on purpose, one that must never leave the server.
public sealed class PacketaConnectionTests : IDisposable
{
    private const string WidgetKey = "widget-key-abc123";
    private const string ApiPassword = "the-api-password-nobody-may-see";

    private readonly ShopForgeApiFactory _factory;
    private readonly RecordingPacketa _packeta = new();
    private readonly WebApplicationFactory<Program> _withPacketa;

    public PacketaConnectionTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withPacketa = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPacketaClient>(_packeta);
            services.AddSingleton<ISecretStore>(new InMemorySecrets());
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withPacketa.Dispose();

    [Fact]
    public async Task A_store_that_has_connected_no_account_is_not_offered_the_carrier()
    {
        var world = await PacketaStoreAsync(connect: false);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());

        Assert.DoesNotContain("z-box", methods.ShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // A connection without its credential is not a connection (D-153): the method stays off the shelf until
    // somebody has set the password, exactly as a payment gateway's does.
    [Fact]
    public async Task A_connection_with_no_password_behind_it_offers_nothing()
    {
        var world = await PacketaStoreAsync(connect: true, withPassword: false);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");

        Assert.DoesNotContain("z-box", methods.ShippingMethods.Select(method => method.Code));
    }

    [Fact]
    public async Task A_connected_store_offers_the_carrier()
    {
        var world = await PacketaStoreAsync(connect: true);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");

        Assert.Contains("z-box", methods.ShippingMethods.Select(method => method.Code));
    }

    // The widget cannot open without it, so it is published on purpose — and it is the only half that is.
    [Fact]
    public async Task The_browser_is_given_the_widget_key()
    {
        var world = await PacketaStoreAsync(connect: true);

        var store = await world.Shopper.GetJsonAsync<StoreView>("/api/storefront/store");
        var published = store.ProviderKeys.SingleOrDefault(key => key.Provider == PacketaShippingProvider.ProviderKey);

        Assert.Equal(WidgetKey, published?.Key);
    }

    // Worth asserting rather than assuming: every answer either side of the shop gives about this store, read
    // as the text that goes over the wire, with the password looked for in all of them.
    [Fact]
    public async Task The_api_password_is_in_no_answer_the_shop_or_the_admin_gives()
    {
        var world = await PacketaStoreAsync(connect: true);
        var storeId = world.Furniture.Store.StoreId;

        var answers = new List<string>
        {
            await world.Shopper.GetStringAsync("/api/storefront/store"),
            await world.Shopper.GetStringAsync("/api/storefront/checkout/methods"),
            await world.Shopper.GetStringAsync("/api/storefront/checkout/pickup-points/z-box"),
            await world.Admin.GetStringAsync($"/api/admin/stores/{storeId}/provider-connections", CancellationToken),
            await world.Admin.GetStringAsync($"/api/admin/stores/{storeId}/shipping-methods", CancellationToken),
        };

        Assert.All(answers, answer => Assert.DoesNotContain(ApiPassword, answer, StringComparison.Ordinal));
        Assert.Contains(answers, answer => answer.Contains(WidgetKey, StringComparison.Ordinal));
    }

    // Only the carrier knows its own boxes, and it is asked with the store's own map key, which is what Packeta's
    // validator takes — never with anything the browser sent, and never with the password (D-213).
    [Fact]
    public async Task The_carrier_is_asked_about_the_point_with_the_stores_own_map_key()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());

        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal("99", _packeta.LastChoice?.PointId);
        Assert.Equal(WidgetKey, _packeta.LastKey);
    }

    // A box chosen for a method priced by the parcel is asked whether it takes that parcel, in Packeta's own units: the
    // heaviest parcel estimated in kilograms, and the cart's largest things side by side in whole centimetres, rounded up
    // (D-220). The map is opened with the same, and a flat method's check asks as it always has.
    [Fact]
    public async Task A_box_by_the_parcel_is_asked_whether_it_takes_the_parcel()
    {
        var world = await PacketaStoreAsync(connect: true);
        var storeId = world.Furniture.Store.StoreId;
        using var flat = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        Assert.Equal(HttpStatusCode.Created, flat.StatusCode);
        var asFlat = _packeta.LastChoice;
        using var method = await world.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/shipping-methods",
            new { Name = "Z-BOX by the parcel", ProviderKey = PacketaShippingProvider.ProviderKey, Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true, Pricing = "perParcel", CarrierService = "packeta-cz-zbox" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);
        using var priced = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{storeId}/shipping-methods/z-box-by-the-parcel/rates",
            new { Source = "own", Bands = new[] { new { UpToGrams = 15_000, Price = 3.50m } } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
        using var added = await world.Shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = world.Furniture.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var methods = await world.Shopper.GetJsonAsync<ParcelMethodsView>("/api/storefront/checkout/methods?country=CZ");
        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", new
        {
            Email = "buyer@example.test",
            Phone = "+420 123 456 789",
            BillingAddress = new { FullName = "Alex Buyer", Line1 = "Wilsonova 8", Line2 = (string?)null, City = "Praha", PostalCode = "110 00", Country = "CZ" },
            ShippingAddress = (object?)null,
            PaymentMethodCode = "bank-transfer",
            ShippingMethodCode = "z-box-by-the-parcel",
            PickupPointCode = "99",
            ExpectedShippingPrice = 3.50m,
        });

        Assert.Equal((null, null, null, null), (asFlat!.WeightKg, asFlat.LengthCm, asFlat.WidthCm, asFlat.DepthCm));
        Assert.Equal(new MapParcelView(2.0m, 45, 40, 9), methods.ShippingMethods.Single(candidate => candidate.Code == "z-box-by-the-parcel").MapParcel);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal(("99", 2.0m, 45, 40, 9), (_packeta.LastChoice!.PointId, _packeta.LastChoice.WeightKg, _packeta.LastChoice.LengthCm, _packeta.LastChoice.WidthCm, _packeta.LastChoice.DepthCm));
    }

    // Packeta's points are numbered, and a parcel can only ever be addressed to a number. Anything else is refused as a
    // point nobody chose, and Packeta is not asked about it.
    [Fact]
    public async Task A_code_that_cannot_name_a_packeta_point_is_refused_without_asking()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout(pickupPointCode: "Z-BOX-99"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Null(_packeta.LastChoice);
    }

    // The map key opens the map and is what the point is checked with, so a store connected without it has a method
    // no order can get through: readiness says so before the store publishes, and the checkout asks nobody (D-213).
    [Fact]
    public async Task A_store_without_the_map_key_takes_no_box_order_and_is_told_before_it_publishes()
    {
        var world = await PacketaStoreAsync(connect: true, withMapKey: false);
        var keyed = await PacketaStoreAsync(connect: true);

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var readiness = await world.Admin.GetFromJsonAsync<ReadinessView>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/readiness", CancellationToken);
        var ready = await keyed.Admin.GetFromJsonAsync<ReadinessView>(
            $"/api/admin/stores/{keyed.Furniture.Store.StoreId}/readiness", CancellationToken);
        var shipping = Assert.Single(readiness!.Checks, check => check.Check == "orders.shipping-connected");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Null(_packeta.LastChoice);
        Assert.False(shipping.IsReady);
        Assert.Contains("\"Z-BOX\" (packeta) has its pickup point chosen in the carrier's map", shipping.Problem!, StringComparison.Ordinal);
        Assert.DoesNotContain("Home delivery", shipping.Problem!, StringComparison.Ordinal);
        Assert.True(Assert.Single(ready!.Checks, check => check.Check == "orders.shipping-connected").IsReady);
    }

    // Nothing about the point comes from the browser except which one it is: the validator is asked, and what
    // it says is what the order keeps.
    [Fact]
    public async Task A_point_the_validator_accepts_is_kept_as_the_validator_describes_it()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.Equal(("cz", "zbox"), (_packeta.LastChoice?.Country, _packeta.LastChoice?.Group));
        Assert.Equal("99", stored.PickupPointCode);
        Assert.Equal("Z-BOX Hlavní nádraží", stored.PickupPointName);
        Assert.Equal("Wilsonova 8", stored.PickupPointAddress!.Line1);
    }

    [Fact]
    public async Task A_point_the_validator_refuses_stops_the_order()
    {
        var world = await PacketaStoreAsync(connect: true);
        _packeta.Says = new PacketaPointCheck(null, ["PickupPointIsFull"]);

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // Silence is not refusal. An order placed on it would be one nobody had checked, so the shopper is asked
    // to come back rather than told their point is wrong.
    [Fact]
    public async Task A_validator_that_does_not_answer_stops_the_order_and_says_so()
    {
        var world = await PacketaStoreAsync(connect: true);
        _packeta.Unreachable = true;

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("The pickup point could not be checked", problem!.Title);
    }

    // The three kinds a shop has to render differently, each saying which it is rather than leaving the page
    // to work it out from an empty list.
    [Fact]
    public async Task Every_method_says_where_its_points_are_chosen()
    {
        var world = await PacketaStoreAsync(connect: true);
        await AddCollectionAsync(world.Admin, world.Furniture.Store.StoreId);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        var choices = methods.ShippingMethods.ToDictionary(method => method.Code, method => method.PickupPointChoice);

        Assert.Equal("carrier-map", choices["z-box"]);
        Assert.Equal("list", choices["collection"]);
        Assert.Equal("none", choices["home-delivery"]);
        Assert.Equal("none", choices["courier"]);
    }

    // The shipping price is the store's row, whatever a browser puts in the body.
    [Fact]
    public async Task A_price_sent_by_the_browser_is_ignored()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", new
        {
            Email = "buyer@example.test",
            Phone = "+420 123 456 789",
            BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
            ShippingAddress = (object?)null,
            PaymentMethodCode = "bank-transfer",
            ShippingMethodCode = "z-box",
            PickupPointCode = "99",
            ShippingPrice = 0m,
            Price = 0m,
        });
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.Equal(59m, stored.ShippingPrice);
    }

    // The client this replaced put the API password in the path of the URL, and the ordinary request logging published
    // it the first time it ran (D-159). The documented check never sends the password at all (D-213); the recording
    // fake cannot see below itself, so this drives the real client over a stubbed socket and reads the log.
    [Fact]
    public async Task The_api_password_is_in_no_log_either()
    {
        var logs = new RecordingLogs();
        using var withTheRealClient = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ISecretStore>(new InMemorySecrets());
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddHttpClient<IPacketaClient, PacketaHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new SilentHandler());
        }));

        var world = await PacketaStoreAsync(connect: true, host: withTheRealClient);
        using var attempted = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());

        // The stub answers nothing useful, so the carrier counts as unreachable and the order is refused.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, attempted.StatusCode);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(ApiPassword, StringComparison.Ordinal));
    }

    // The whole way through the real client and a real socket: a listener on this machine stands in for Packeta and
    // answers with the example in Packeta's own reference. The question arrives as the reference describes it, with the
    // map key, and the order keeps the point as the answer describes it. The HTTP client's span records the address it
    // called, which an exporter sends on to Application Insights, so every finished span is read for the password too
    // (D-199, D-213).
    [Fact]
    public async Task The_point_check_goes_out_as_documented_and_the_password_is_in_no_trace()
    {
        using var packeta = new PacketaOnThisMachine(HttpStatusCode.OK, """
            {"isValid":true,"point":{"name":"Z-BOX","address":{"street":"Českomoravská 2408","city":"Praha ","zip":"190 00","country":"cz"},"group":"zbox"},"errors":[]}
            """);
        var spans = new ConcurrentQueue<string>();
        using var withTheRealClient = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Shipping:Packeta:WidgetAddress", packeta.Address);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISecretStore>(new InMemorySecrets());
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new FinishedSpans(spans)));
            });
        });

        var world = await PacketaStoreAsync(connect: true, host: withTheRealClient);
        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);
        var question = JsonNode.Parse(packeta.LastBody!)!;

        Assert.Equal(("POST", "/v6/pps/api/widget/v1/validate"), (packeta.LastMethod, packeta.LastPath));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(packeta.LastBody!), packeta.LastLength);
        Assert.Equal(
            """{"apiKey":"widget-key-abc123","point":{"id":"99"},"options":{"country":"cz","vendors":[{"country":"cz","group":"zbox"}]}}""",
            question.ToJsonString());
        Assert.Equal(("99", "Z-BOX"), (stored.PickupPointCode, stored.PickupPointName));
        Assert.Equal(("Českomoravská 2408", "Praha", "190 00", "CZ"), (stored.PickupPointAddress!.Line1, stored.PickupPointAddress.City, stored.PickupPointAddress.PostalCode, stored.PickupPointAddress.Country));
        Assert.DoesNotContain(ApiPassword, packeta.LastBody!, StringComparison.Ordinal);
        Assert.Contains(spans, span => span.Contains(packeta.Authority, StringComparison.Ordinal));
        Assert.DoesNotContain(spans, span => span.Contains(ApiPassword, StringComparison.Ordinal));
    }

    // A parcel booked in Packeta's portal is recorded by the number the portal shows, and the shopper is given Packeta's
    // own tracking page for it, in the store's language. A number that is not a Packeta packet's gets no link.
    [Theory]
    [InlineData("Z 123 4567 890", "https://tracking.packeta.com/en/?id=1234567890")]
    [InlineData("PKG-1", null)]
    public async Task A_packeta_shipment_links_to_packetas_tracking_page(string trackingNumber, string? link)
    {
        var world = await PacketaStoreAsync(connect: true);
        var storeId = world.Furniture.Store.StoreId;
        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        using var paid = await world.Admin.PostAsync($"/api/admin/stores/{storeId}/orders/{order.Number}/payment", null, CancellationToken);
        using var shipped = await world.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/orders/{order.Number}/shipment", new { TrackingNumber = trackingNumber }, CancellationToken);
        var stored = await StoredAsync(world, order.Number);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (paid.StatusCode, shipped.StatusCode));
        Assert.Equal(("Packeta", trackingNumber, link), (stored.Shipment!.Carrier, stored.Shipment.TrackingNumber, stored.Shipment.TrackingUrl));
    }

    // The same carrier, no map, nowhere to choose. Everything Packeta asks for about the recipient — a name,
    // a telephone number, an e-mail address, a street line, a town and a postal code — is already required of
    // every order, so a doorstep delivery needs no gate of its own, only proof that it holds.
    [Fact]
    public async Task The_same_carrier_delivers_to_the_door_with_nothing_to_choose()
    {
        var world = await PacketaStoreAsync(connect: true);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        var home = methods.ShippingMethods.Single(method => method.Code == "home-delivery");
        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", HomeDelivery());
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.False(home.RequiresPickupPoint);
        Assert.Equal("none", home.PickupPointChoice);
        Assert.Equal(PacketaShippingProvider.ProviderKey, stored.ShippingProviderKey);
        Assert.Equal("+420 123 456 789", stored.Phone);
    }

    // The structural half of "a box is not a home": nothing of the pickup point is left on the order, and the
    // carrier is never asked about one.
    [Fact]
    public async Task A_doorstep_order_keeps_no_pickup_point_and_asks_about_none()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", HomeDelivery());
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.Null(stored.PickupPointCode);
        Assert.Null(stored.PickupPointName);
        Assert.Null(stored.PickupPointAddress);
        Assert.Null(_packeta.LastChoice);
    }

    // A parcel going to a door still needs somebody to telephone about it (D-144).
    [Fact]
    public async Task A_doorstep_order_without_a_telephone_number_is_refused()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", HomeDelivery(phone: null));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // A point chosen for a box has no meaning for a doorstep, so sending one changes nothing: the order goes
    // to the address, and the carrier is not asked about the point.
    [Fact]
    public async Task A_point_sent_with_a_doorstep_order_is_ignored()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", HomeDelivery(pickupPointCode: "99"));
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.Null(stored.PickupPointCode);
        Assert.Null(_packeta.LastChoice);
    }

    // Whoever packs the parcel reads one answer: who is carrying it, and where it is going. A box has its own
    // address; a doorstep uses the shopper's.
    [Fact]
    public async Task The_admin_reads_the_carrier_and_the_destination_for_either_method()
    {
        var world = await PacketaStoreAsync(connect: true);
        var storeId = world.Furniture.Store.StoreId;

        using var toABox = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());
        var boxed = await world.Shopper.ReadAsync<PlacedOrder>(toABox, HttpStatusCode.Created);
        using var again = await world.Shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = world.Furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var toADoor = await world.Shopper.PostAsync("/api/storefront/checkout", HomeDelivery());
        var delivered = await world.Shopper.ReadAsync<PlacedOrder>(toADoor, HttpStatusCode.Created);

        var box = await world.Admin.GetFromJsonAsync<AdminOrderView>(
            $"/api/admin/stores/{storeId}/orders/{boxed.Number}", CancellationToken);
        var door = await world.Admin.GetFromJsonAsync<AdminOrderView>(
            $"/api/admin/stores/{storeId}/orders/{delivered.Number}", CancellationToken);

        Assert.Equal(PacketaShippingProvider.ProviderKey, box!.Carrier);
        Assert.Equal("Z-BOX Hlavní nádraží, Wilsonova 8, Praha", box.PickupPoint);
        Assert.Equal(PacketaShippingProvider.ProviderKey, door!.Carrier);
        Assert.Null(door.PickupPoint);
        Assert.Equal("Praha", door.ShippingAddress.City);
        Assert.Equal("+420 123 456 789", door.Phone);
    }

    private static object HomeDelivery(string? phone = "+420 123 456 789", string? pickupPointCode = null) => new
    {
        Email = "buyer@example.test",
        Phone = phone,
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "Wilsonova 8", Line2 = (string?)null, City = "Praha", PostalCode = "110 00", Country = "CZ" },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = "home-delivery",
        PickupPointCode = pickupPointCode,
    };

    private static object PacketaCheckout(string pickupPointCode = "99") => new
    {
        Email = "buyer@example.test",
        Phone = "+420 123 456 789",
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = "z-box",
        PickupPointCode = pickupPointCode,
    };

    private async Task<PacketaWorld> PacketaStoreAsync(
        bool connect,
        bool withPassword = true,
        bool withMapKey = true,
        WebApplicationFactory<Program>? host = null)
    {
        var app = host ?? _withPacketa;
        var furniture = await FurnitureStore.CreateAsync(_factory);
        var admin = await TestUsers.LoginAsync(app, await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId));
        var storeId = furniture.Store.StoreId;

        if (connect)
        {
            using var saved = await admin.PutAsJsonAsync(
                $"/api/admin/stores/{storeId}/provider-connections/{PacketaShippingProvider.ProviderKey}",
                new { MerchantId = "sender-identification", Environment = "Test", IsActive = true, PublishableKey = withMapKey ? WidgetKey : null },
                CancellationToken);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            if (withPassword)
            {
                using var kept = await admin.PutAsJsonAsync(
                    $"/api/admin/stores/{storeId}/provider-connections/{PacketaShippingProvider.ProviderKey}/secret",
                    new { Secret = ApiPassword },
                    CancellationToken);
                Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);
            }
        }

        await AddMethodAsync(admin, storeId, "Z-BOX", requiresPickupPoint: true);
        await AddMethodAsync(admin, storeId, "Home delivery", requiresPickupPoint: false);

        var shopper = new StorefrontApi(app, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        return new PacketaWorld(furniture, admin, shopper);
    }

    private Task<Order> StoredAsync(PacketaWorld world, string number) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, CancellationToken));

    // A method of the store's own that collects from a counter: its points are rows we keep, so they come as
    // a list.
    private async Task AddCollectionAsync(HttpClient admin, Guid storeId)
    {
        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/shipping-methods",
            new { Name = "Collection", ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, method.StatusCode);
    }

    private async Task AddMethodAsync(HttpClient admin, Guid storeId, string name, bool requiresPickupPoint)
    {
        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/shipping-methods",
            new
            {
                Name = name,
                ProviderKey = PacketaShippingProvider.ProviderKey,
                Price = 59m,
                VatRate = 21m,
                IsActive = true,
                RequiresPickupPoint = requiresPickupPoint,
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, method.StatusCode);
    }

    private sealed record PacketaWorld(FurnitureStore Furniture, HttpClient Admin, StorefrontApi Shopper);

    private sealed record ProblemView(string Title);

    private sealed record AdminOrderView(string Number, string? Carrier, string? Phone, string? PickupPoint, AdminAddressView ShippingAddress);

    private sealed record AdminAddressView(string FullName, string Line1, string City, string PostalCode, string Country);

    private sealed record MethodsView(List<ShippingMethodView> ShippingMethods);

    private sealed record ParcelMethodsView(List<ParcelMethodView> ShippingMethods);

    private sealed record ParcelMethodView(string Code, MapParcelView? MapParcel);

    private sealed record MapParcelView(decimal WeightKg, int? LengthCm, int? WidthCm, int? DepthCm);

    private sealed record ShippingMethodView(string Code, string Name, decimal Price, bool RequiresPickupPoint, string PickupPointChoice);

    private sealed record StoreView(string Name, List<ProviderKeyView> ProviderKeys);

    private sealed record ProviderKeyView(string Provider, string Key);

    private sealed record ReadinessView(bool IsReady, List<ReadinessCheckView> Checks);

    private sealed record ReadinessCheckView(string Check, bool IsReady, string? Problem);
}

// Records what would have gone to Packeta, so every later slice is testable without the network. It can also
// refuse a point, and fail to answer at all, which are two different things the checkout must tell apart.
internal sealed class RecordingPacketa : IPacketaClient
{
    public string? LastKey { get; private set; }

    public PacketaPointChoice? LastChoice { get; private set; }

    public PacketaPointCheck Says { get; set; } = new(new PacketaPoint("99", "Z-BOX Hlavní nádraží", "Wilsonova 8", "Praha", "110 00", "CZ"), []);

    public bool Unreachable { get; set; }

    public Task<PacketaPointCheck> ValidatePointAsync(string apiKey, PacketaPointChoice choice, CancellationToken cancellationToken)
    {
        LastKey = apiKey;
        LastChoice = choice;

        return Unreachable
            ? Task.FromException<PacketaPointCheck>(new HttpRequestException("Packeta is unreachable."))
            : Task.FromResult(Says);
    }
}

// Answers every call without a network, so the real Packeta client can be exercised for what it logs.
// Every span the host finished, as the exporter would receive it: its name and every attribute.
internal sealed class FinishedSpans(ConcurrentQueue<string> spans) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data) =>
        spans.Enqueue($"{data.DisplayName} {string.Join(' ', data.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))}");
}

// Packeta on a port of this machine, answering every request the same way and keeping the last one, so that a real
// socket — and with it the HTTP client's own span — is involved.
internal sealed class PacketaOnThisMachine : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly HttpStatusCode _status;
    private readonly string _answer;

    public PacketaOnThisMachine(HttpStatusCode status, string answer)
    {
        _status = status;
        _answer = answer;

        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();

        Authority = $"localhost:{port}";
        _listener.Prefixes.Add($"http://{Authority}/");
        _listener.Start();
        _ = AnswerAsync();
    }

    public string Authority { get; }

    // Where the widget's own address would be.
    public string Address => $"http://{Authority}/v6/";

    public string? LastMethod { get; private set; }

    public string? LastPath { get; private set; }

    public string? LastBody { get; private set; }

    // What the request said its length was, or -1 for a body sent in chunks.
    public long LastLength { get; private set; }

    public void Dispose() => _listener.Close();

    private async Task AnswerAsync()
    {
        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    LastBody = await reader.ReadToEndAsync();
                }

                LastMethod = context.Request.HttpMethod;
                LastLength = context.Request.ContentLength64;
                LastPath = context.Request.Url?.AbsolutePath;
                context.Response.StatusCode = (int)_status;
                context.Response.ContentType = "application/json";
                var answer = System.Text.Encoding.UTF8.GetBytes(_answer);
                await context.Response.OutputStream.WriteAsync(answer);
                context.Response.Close();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
        }
    }
}

internal sealed class SilentHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}

// Everything the host logged, as the text that would have reached an operator's console.
internal sealed class RecordingLogs : ILoggerProvider
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

    public void Dispose()
    {
    }

    private sealed class Recorder(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}
