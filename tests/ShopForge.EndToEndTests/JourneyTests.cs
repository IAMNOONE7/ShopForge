using System.Net;
using System.Text.Json;

namespace ShopForge.EndToEndTests;

// The harness proved against itself: each test starts a run of its own on the stack, shops as a browser would through
// the API, pays on the gateway's own page, and then reads what the run left behind. They stand for the journeys the
// browser tests make, without the browser, so that a broken harness is found here rather than by a flaky page (D-203).
public sealed class JourneyTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_is_a_company_of_its_own_that_no_other_run_can_read()
    {
        var stack = Stack.Required();
        var first = await stack.StartRunAsync(CancellationToken);
        var second = await stack.StartRunAsync(CancellationToken);
        var placed = await PlaceAsync(stack, first, "M");

        foreach (var store in first.GetProperty("stores").EnumerateArray())
        {
            using var browser = new Shopper(stack.Api, store.GetProperty("host").GetString()!);
            var (status, served) = await browser.SendAsync(HttpMethod.Get, "api/storefront/store", null, CancellationToken);

            Assert.Equal((HttpStatusCode.OK, store.GetProperty("id").GetGuid()), (status, served.GetProperty("id").GetGuid()));
        }

        var (read, _) = await stack.RigAsync(HttpMethod.Get, $"runs/{Id(second)}/stores/{placed.StoreId}/orders/{placed.Number}", null, CancellationToken);
        var (_, post) = await stack.RigAsync(HttpMethod.Get, $"runs/{Id(second)}/mail", null, CancellationToken);

        Assert.NotEqual(StoreId(first), StoreId(second));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("toolShop").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, read);
        Assert.DoesNotContain(post.EnumerateArray(), letter => letter.GetProperty("to").GetString()!.EndsWith(Domain(first), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_guest_pays_on_the_gateway_page_and_the_order_is_paid_once()
    {
        var stack = Stack.Required();
        var run = await stack.StartRunAsync(CancellationToken);
        var placed = await PlaceAsync(stack, run, "M");

        var back = await placed.Shopper.ChooseOnGatewayAsync(placed.PayPage, "paid", CancellationToken);
        var order = await OrderAsync(stack, run, placed);
        var stock = await StockAsync(stack, run, "M");
        var letters = await LettersAsync(stack, run, placed.Buyer, expected: 2);

        Assert.EndsWith($"/order/{placed.Number}", back.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Paid", order.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(["Paid"], Statuses(order));
        Assert.Equal(1, order.GetProperty("answersRecorded").GetInt32());
        Assert.Equal((4, 0), (stock.GetProperty("onHand").GetInt32(), stock.GetProperty("reserved").GetInt32()));
        Assert.Contains(letters, subject => subject.Contains("Payment received", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declined_payment_cancels_the_order_and_gives_its_stock_back()
    {
        var stack = Stack.Required();
        var run = await stack.StartRunAsync(CancellationToken);
        var placed = await PlaceAsync(stack, run, "L");

        var back = await placed.Shopper.ChooseOnGatewayAsync(placed.PayPage, "cancelled", CancellationToken);
        var order = await OrderAsync(stack, run, placed);
        var stock = await StockAsync(stack, run, "L");

        Assert.EndsWith("/cart", back.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Cancelled", order.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(["Failed"], Statuses(order));
        Assert.Equal((5, 0), (stock.GetProperty("onHand").GetInt32(), stock.GetProperty("reserved").GetInt32()));
    }

    // The gateway took the money and its notification never came. When the reservation runs out, the sweep asks the
    // gateway before letting the order go, and finds it paid (D-199). The stack's clock is moved past the reservation
    // rather than waited out.
    [Fact]
    public async Task A_payment_whose_notification_is_lost_is_found_when_the_order_runs_out()
    {
        var stack = Stack.Required();
        var run = await stack.StartRunAsync(CancellationToken);
        await BehaveAsync(stack, run, new { push = "drop" });
        var placed = await PlaceAsync(stack, run, "S");

        await placed.Shopper.ChooseOnGatewayAsync(placed.PayPage, "paid", CancellationToken);
        var waiting = await OrderAsync(stack, run, placed);
        var (moved, _) = await stack.RigAsync(HttpMethod.Post, $"runs/{Id(run)}/clock", new { advanceMinutes = 40 }, CancellationToken);
        var (swept, _) = await stack.RigAsync(HttpMethod.Post, $"runs/{Id(run)}/passes/expired-orders", null, CancellationToken);
        var order = await OrderAsync(stack, run, placed);

        Assert.Equal("AwaitingPayment", waiting.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NoContent), (moved, swept));
        Assert.Equal("Paid", order.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(["Paid"], Statuses(order));
    }

    // A status Comgate does not document is recorded and moves nothing (D-196).
    [Fact]
    public async Task A_status_nobody_wrote_code_for_moves_nothing()
    {
        var stack = Stack.Required();
        var run = await stack.StartRunAsync(CancellationToken);
        var placed = await PlaceAsync(stack, run, "M");
        var transaction = placed.PayPage[(placed.PayPage.LastIndexOf('/') + 1)..];

        var (changed, _) = await stack.RigAsync(
            HttpMethod.Post, $"runs/{Id(run)}/comgate/transactions/{transaction}", new { status = "REVERSAL_PENDING", push = true }, CancellationToken);
        var order = await OrderAsync(stack, run, placed);

        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal("AwaitingPayment", order.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(["Unrecognised"], Statuses(order));
    }

    // A run asked for with the tool shop has the development data's Czech shop as a third store of its company, built
    // through the admin API and the import, beside the two every run has. A shopper typing without accents is offered
    // its screwdrivers, and the hidden sample is not for sale (D-227).
    [Fact]
    public async Task A_run_with_the_tool_shop_suggests_what_is_typed_without_accents()
    {
        var stack = Stack.Required();
        var run = await stack.StartRunAsync(toolShop: true, CancellationToken);
        var toolShop = run.GetProperty("toolShop");
        using var browser = new Shopper(stack.Api, toolShop.GetProperty("host").GetString()!);

        var (suggesting, suggested) = await browser.SendAsync(HttpMethod.Get, "api/storefront/search/suggest?q=sroubovak", null, CancellationToken);
        var (listing, everything) = await browser.SendAsync(HttpMethod.Get, "api/storefront/products?pageSize=100", null, CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (suggesting, listing));
        Assert.Equal("sroubovaky", suggested.GetProperty("categories")[0].GetProperty("slug").GetString());
        Assert.StartsWith("Šroubovák", suggested.GetProperty("products")[0].GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(39, everything.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, run.GetProperty("stores").GetArrayLength());
        Assert.DoesNotContain(run.GetProperty("stores").EnumerateArray(), store => store.GetProperty("id").GetGuid() == toolShop.GetProperty("id").GetGuid());
    }

    // A guest's order of one T-shirt in the size given, placed on the run's first store with Comgate.
    private static async Task<Placed> PlaceAsync(Stack stack, JsonElement run, string size)
    {
        var store = run.GetProperty("stores")[0];
        var storeId = store.GetProperty("id").GetGuid();
        var tee = run.GetProperty("products").EnumerateArray().Single(product => product.GetProperty("slug").GetString() == "t-shirt");
        var variant = tee.GetProperty("variants").EnumerateArray().Single(candidate => candidate.GetProperty("size").GetString() == size);
        var buyer = $"buyer-{Guid.NewGuid():N}@{Domain(run)}";
        var shopper = new Shopper(stack.Api, store.GetProperty("host").GetString()!);

        var (added, _) = await shopper.SendAsync(
            HttpMethod.Post,
            "api/storefront/cart/items",
            new { storeProductId = tee.GetProperty("listings").GetProperty(storeId.ToString()).GetGuid(), variantId = variant.GetProperty("id").GetGuid(), quantity = 1 },
            CancellationToken);
        var (status, order) = await shopper.SendAsync(
            HttpMethod.Post,
            "api/storefront/checkout",
            new
            {
                email = buyer,
                phone = "+420 777 123 456",
                billingAddress = new { fullName = "Eva Kupující", line1 = "Náměstí 1", line2 = (string?)null, city = "Brno", postalCode = "602 00", country = "CZ" },
                shippingAddress = (object?)null,
                paymentMethodCode = "comgate",
                shippingMethodCode = "personal-pickup",
                pickupPointCode = (string?)null,
            },
            CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Created), (added, status));

        return new Placed(shopper, storeId, order.GetProperty("number").GetString()!, order.GetProperty("redirectUrl").GetString()!, buyer);
    }

    private static async Task<JsonElement> OrderAsync(Stack stack, JsonElement run, Placed placed)
    {
        var (status, order) = await stack.RigAsync(HttpMethod.Get, $"runs/{Id(run)}/stores/{placed.StoreId}/orders/{placed.Number}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, status);

        return order;
    }

    private static async Task<JsonElement> StockAsync(Stack stack, JsonElement run, string size)
    {
        var sku = run.GetProperty("products").EnumerateArray()
            .SelectMany(product => product.GetProperty("variants").EnumerateArray())
            .Single(variant => variant.GetProperty("size").GetString() == size)
            .GetProperty("sku").GetString();
        var (status, stock) = await stack.RigAsync(HttpMethod.Get, $"runs/{Id(run)}/stock/{sku}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, status);

        return stock;
    }

    // Letters go out through the outbox, a second at a time on the stack, so they are waited for, briefly.
    private static async Task<List<string>> LettersAsync(Stack stack, JsonElement run, string to, int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var (_, post) = await stack.RigAsync(HttpMethod.Get, $"runs/{Id(run)}/mail?to={Uri.EscapeDataString(to)}", null, CancellationToken);
            var subjects = post.EnumerateArray().Select(letter => letter.GetProperty("subject").GetString()!).ToList();

            if (subjects.Count >= expected)
            {
                return subjects;
            }

            await Task.Delay(200, CancellationToken);
        }

        Assert.Fail($"Fewer than {expected} letters reached {to}.");

        return [];
    }

    private static async Task BehaveAsync(Stack stack, JsonElement run, object behaviour)
    {
        var (status, answer) = await stack.RigAsync(HttpMethod.Put, $"runs/{Id(run)}/comgate/behaviour", behaviour, CancellationToken);
        Assert.True(status == HttpStatusCode.OK, $"The gateway would not behave so: {answer}");
    }

    private static List<string> Statuses(JsonElement order) =>
        [.. order.GetProperty("attempts").EnumerateArray().Select(attempt => attempt.GetProperty("status").GetString()!)];

    private static string Id(JsonElement run) => run.GetProperty("id").GetString()!;

    private static string Domain(JsonElement run) => run.GetProperty("emailDomain").GetString()!;

    private static Guid StoreId(JsonElement run) => run.GetProperty("stores")[0].GetProperty("id").GetGuid();

    private sealed record Placed(Shopper Shopper, Guid StoreId, string Number, string PayPage, string Buyer);
}
