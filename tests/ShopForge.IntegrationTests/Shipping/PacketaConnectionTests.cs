using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    // Only the carrier knows its own boxes, and it is asked with the password the store kept — never with
    // anything the browser sent.
    [Fact]
    public async Task The_carrier_is_asked_about_the_point_with_the_stores_own_password()
    {
        var world = await PacketaStoreAsync(connect: true);

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", PacketaCheckout());

        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal("99", _packeta.LastChoice?.PointId);
        Assert.Equal(ApiPassword, _packeta.LastAccount?.ApiPassword);
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

        Assert.Equal(("cz", "zbox"), (_packeta.LastChoice?.Country, _packeta.LastChoice?.Vendor));
        Assert.Equal("99", stored.PickupPointCode);
        Assert.Equal("Z-BOX Hlavní nádraží", stored.PickupPointName);
        Assert.Equal("Wilsonova 8", stored.PickupPointAddress!.Line1);
    }

    [Fact]
    public async Task A_point_the_validator_refuses_stops_the_order()
    {
        var world = await PacketaStoreAsync(connect: true);
        _packeta.Says = null;

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

    // Packeta puts the API password in the path of the URL, so the URL is itself a credential and the ordinary
    // HTTP request logging published it the first time this ran against the real client. The recording fake
    // cannot catch that, so this one drives the real client over a stubbed socket and reads the log.
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

    private static object PacketaCheckout() => new
    {
        Email = "buyer@example.test",
        Phone = "+420 123 456 789",
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = "z-box",
        PickupPointCode = "99",
    };

    private async Task<PacketaWorld> PacketaStoreAsync(
        bool connect,
        bool withPassword = true,
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
                new { MerchantId = "sender-identification", Environment = "Test", IsActive = true, PublishableKey = WidgetKey },
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

    private sealed record ShippingMethodView(string Code, string Name, decimal Price, bool RequiresPickupPoint, string PickupPointChoice);

    private sealed record StoreView(string Name, List<ProviderKeyView> ProviderKeys);

    private sealed record ProviderKeyView(string Provider, string Key);
}

// Records what would have gone to Packeta, so every later slice is testable without the network. It can also
// refuse a point, and fail to answer at all, which are two different things the checkout must tell apart.
internal sealed class RecordingPacketa : IPacketaClient
{
    public PacketaAccount? LastAccount { get; private set; }

    public PacketaPointChoice? LastChoice { get; private set; }

    public PacketaPoint? Says { get; set; } = new("99", "Z-BOX Hlavní nádraží", "Wilsonova 8", "Praha", "110 00", "CZ");

    public bool Unreachable { get; set; }

    public Task<PacketaPoint?> ValidatePointAsync(PacketaAccount account, PacketaPointChoice choice, CancellationToken cancellationToken)
    {
        LastAccount = account;
        LastChoice = choice;

        return Unreachable
            ? Task.FromException<PacketaPoint?>(new HttpRequestException("Packeta is unreachable."))
            : Task.FromResult(Says);
    }
}

// Answers every call without a network, so the real Packeta client can be exercised for what it logs.
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
