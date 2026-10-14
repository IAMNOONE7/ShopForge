using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Admin;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// What Comgate paid out, laid beside what ShopForge recorded as paid through it, with every difference named and
// marked explained or not (D-198). The two sides are matched on Comgate's transaction and counted on Prague days.
public sealed class ReconciliationTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private const int Wait = AdminReconciliationEndpoints.SettlementWait;

    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly InMemorySecrets _secrets = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public ReconciliationTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withComgate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IComgatePayments>(_comgate);
            services.AddSingleton<ISecretStore>(_secrets);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Prague).DateTime);

    public void Dispose() => _withComgate.Dispose();

    [Fact]
    public async Task A_payment_paid_out_for_what_was_asked_leaves_nothing_to_explain()
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn);
        _comgate.PaysOut(paidOn.AddDays(1), 101, Line(world, paidOn));
        await ImportAsync(world);

        var report = await ReportAsync(world, paidOn, paidOn.AddDays(1));
        var day = report.Days[0];

        Assert.Equal((0, Today.AddDays(-StatementImport.DaysBehind)), (report.Unexplained, report.StatementsThrough));
        Assert.Empty(report.Differences);
        Assert.Equal(
            (paidOn, 1, world.Amount, 1, world.Amount, 0.35m, world.Amount - 0.35m),
            (day.Date, day.Paid, day.PaidAmount, day.Settled, day.SettledAmount, day.Fees, day.Transferred));
    }

    // Comgate pays out on its own schedule, so a payment in no statement yet is waited for, and only a payment in no
    // statement for the whole wait is reported missing. The boundary is exact: the seventh day is the last waited.
    [Fact]
    public async Task A_payment_in_no_statement_is_waited_for_and_then_reported_missing()
    {
        var through = Today.AddDays(-StatementImport.DaysBehind);
        var recent = await PaidAsync(through.AddDays(-(Wait - 1)));
        var old = await PaidAsync(through.AddDays(-Wait));
        await ImportAsync(recent);
        await ImportAsync(old);

        var waited = (await ReportAsync(recent, Today.AddDays(-14), Today)).Differences.Single();
        var missing = (await ReportAsync(old, Today.AddDays(-14), Today)).Differences.Single();

        Assert.Equal(("awaitingSettlement", true), (waited.Kind, waited.Explained));
        Assert.Equal(("notSettled", false), (missing.Kind, missing.Explained));
        Assert.Equal((old.Number, old.TransactionId, old.Amount, null), (missing.OrderNumber, missing.TransactionId, missing.Expected, missing.Settled));
    }

    [Theory]
    [InlineData(1, "EUR")]
    [InlineData(0, "CZK")]
    public async Task A_payment_paid_out_for_another_amount_or_in_another_currency_is_reported(int more, string currency)
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn);
        _comgate.PaysOut(paidOn.AddDays(1), 101, Line(world, paidOn, amount: world.Amount + more) with { Currency = currency });
        await ImportAsync(world);

        var difference = (await ReportAsync(world, paidOn, paidOn)).Differences.Single();

        Assert.Equal(("amountDiffers", false), (difference.Kind, difference.Explained));
        Assert.Equal((world.Amount, world.Amount + more), (difference.Expected, difference.Settled));
    }

    // Nothing has been read yet — a new shop, or an import that has not run — so nothing can be called missing.
    [Fact]
    public async Task Before_any_statement_is_read_a_payment_is_waited_for()
    {
        var paidOn = Today.AddDays(-20);
        var world = await PaidAsync(paidOn);

        var report = await ReportAsync(world, paidOn, paidOn);
        var difference = report.Differences.Single();

        Assert.Equal((null, "awaitingSettlement", true), (report.StatementsThrough, difference.Kind, difference.Explained));
        Assert.Equal("Not in a statement yet: none has been read.", difference.Detail);
    }

    [Fact]
    public async Task A_payment_paid_out_twice_is_reported()
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn);
        _comgate.PaysOut(paidOn.AddDays(1), 101, Line(world, paidOn));
        _comgate.PaysOut(paidOn.AddDays(2), 102, Line(world, paidOn));
        await ImportAsync(world);

        var difference = (await ReportAsync(world, paidOn, paidOn)).Differences.Single();

        Assert.Equal(("settledTwice", false), (difference.Kind, difference.Explained));
    }

    // Money Comgate paid out for a payment ShopForge never saw finish: the push was lost and the sweep gave up, or
    // the status was one nobody knew. The order was never paid, and this is how anybody finds out.
    [Fact]
    public async Task Money_paid_out_for_a_payment_shopforge_never_saw_paid_is_reported()
    {
        var paidOn = Today.AddDays(-10);
        var world = await StartedAsync();
        await PayAsync(world, "PENDING");
        await PaidOnAsync(world, paidOn);
        _comgate.PaysOut(paidOn.AddDays(1), 101, Line(world, paidOn));
        await ImportAsync(world);

        var report = await ReportAsync(world, paidOn, paidOn);
        var difference = report.Differences.Single();

        Assert.Equal(("settledButNotPaid", false, world.Number), (difference.Kind, difference.Explained, difference.OrderNumber));
        Assert.Contains("as pending", difference.Detail, StringComparison.Ordinal);
        Assert.Equal((0, 1), (report.Days[0].Paid, report.Days[0].Settled));
    }

    // Money taken for an order that could no longer take it is a difference ShopForge already knows about and keeps
    // to give back (D-193): explained, and shown with where the giving back stands.
    [Fact]
    public async Task A_payment_kept_for_an_order_that_could_not_take_it_is_explained()
    {
        var paidOn = Today.AddDays(-10);
        var world = await StartedAsync();
        await CancelAsync(world);
        await PayAsync(world);
        await PaidOnAsync(world, paidOn);
        _comgate.PaysOut(paidOn.AddDays(1), 101, Line(world, paidOn));
        await ImportAsync(world);

        var report = await ReportAsync(world, paidOn, paidOn);
        var difference = report.Differences.Single();

        Assert.Equal(0, report.Unexplained);
        Assert.Equal(("keptForRefund", true, world.Number), (difference.Kind, difference.Explained, difference.OrderNumber));
        Assert.Contains("(open)", difference.Detail, StringComparison.Ordinal);
    }

    // One account can sell for several stores, and its statement carries all of them. A sister store's payment is
    // that store's to reconcile; a payment no store here started is a difference.
    [Fact]
    public async Task A_transaction_nobody_here_started_is_reported_and_a_sister_stores_is_left_to_it()
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn);
        var sister = await StartedAsync(merchantId: world.MerchantId);
        _comgate.PaysOut(
            paidOn.AddDays(1),
            101,
            Line(world, paidOn),
            Line(sister, paidOn),
            Line(world, paidOn, transactionId: "TRNS-NOBO-DY00"));
        await ImportAsync(world);

        var difference = (await ReportAsync(world, paidOn, paidOn)).Differences.Single();

        Assert.Equal(("notStartedHere", false, "TRNS-NOBO-DY00"), (difference.Kind, difference.Explained, difference.TransactionId));
    }

    // A refund, a chargeback, whatever else Comgate puts on a statement: the reference describes none of them, so
    // they are reported for a person to read rather than read by a guess. So is a line that cannot be read at all.
    [Fact]
    public async Task Lines_nobody_has_written_code_for_or_that_cannot_be_read_are_reported()
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn);
        _comgate.PaysOut(
            paidOn.AddDays(1),
            101,
            Line(world, paidOn),
            Line(world, paidOn) with { Type = 2, ConfirmedAmount = "-100,00" },
            Line(world, paidOn, transactionId: "TRNS-UNRE-AD00") with { ConfirmedAmount = "deset" });
        await ImportAsync(world);

        var report = await ReportAsync(world, paidOn, paidOn);

        Assert.Equal(
            [("unreadableLine", "TRNS-UNRE-AD00"), ("unrecognisedLine", world.TransactionId)],
            report.Differences.Select(difference => (difference.Kind, difference.TransactionId)));
        Assert.Equal(2, report.Unexplained);
        Assert.Contains("\"deset\"", report.Differences[0].Detail, StringComparison.Ordinal);
    }

    // A day is read once, two days after it ended, from the store's first payment on: never again, and never sooner.
    [Fact]
    public async Task Statements_are_read_once_a_day_and_two_days_behind()
    {
        var world = await PaidAsync(Today.AddDays(-5));

        var first = await ImportAsync(world);
        var asked = _comgate.StatementDaysAsked.ToList();
        var again = await ImportAsync(world);

        Assert.Equal(Enumerable.Range(0, 4).Select(offset => Today.AddDays(-5 + offset)), asked);
        Assert.Equal((4, 0), (first, again));
        Assert.Equal(asked, _comgate.StatementDaysAsked);
    }

    // The first read of a store with a long history goes back two months, a month at a time, and the rest follows.
    [Fact]
    public async Task A_stores_first_read_goes_back_two_months_a_month_at_a_time()
    {
        var world = await PaidAsync(Today.AddDays(-70));

        var first = await ImportAsync(world);
        var asked = _comgate.StatementDaysAsked.ToList();
        var rest = await ImportAsync(world);

        Assert.Equal((31, Today.AddDays(-StatementImport.FirstLook), Today.AddDays(-StatementImport.FirstLook + 30)), (first, asked[0], asked[^1]));
        Assert.Equal(StatementImport.FirstLook - StatementImport.DaysBehind + 1 - 31, rest);
    }

    // Comgate settles production payments only. A test account's payments are counted aside, and the account is
    // never asked for a statement it cannot have.
    [Fact]
    public async Task A_test_payment_is_counted_aside_and_a_test_account_is_never_asked()
    {
        var paidOn = Today.AddDays(-10);
        var world = await PaidAsync(paidOn, environment: "Test");
        await ImportAsync(world);

        var report = await ReportAsync(world, paidOn, paidOn);

        Assert.Empty(_comgate.StatementDaysAsked);
        Assert.Equal((1, 0, null), (report.TestPayments, report.Differences.Count, report.StatementsThrough));
    }

    [Fact]
    public async Task The_books_are_read_by_the_people_who_run_the_store_and_over_a_month_at_most()
    {
        var world = await StartedAsync();
        using var cataloguer = await TestUsers.LoginAsync(
            _withComgate, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId, TenantRole.CatalogManager));
        using var owner = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId));
        var path = $"/api/admin/stores/{world.Store.Store.StoreId}/reconciliation";

        using var forbidden = await cataloguer.GetAsync(path, CancellationToken);
        using var tooLong = await owner.GetAsync($"{path}?from={Iso(Today.AddDays(-31))}&to={Iso(Today)}", CancellationToken);
        using var unknown = await owner.GetAsync($"{path}?provider=nobody", CancellationToken);
        using var month = await owner.GetAsync($"{path}?from={Iso(Today.AddDays(-30))}&to={Iso(Today)}", CancellationToken);

        Assert.Equal(
            (HttpStatusCode.Forbidden, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.OK),
            (forbidden.StatusCode, tooLong.StatusCode, unknown.StatusCode, month.StatusCode));
    }

    private static ComgateTransferLine Line(ComgateOrder world, DateOnly paidOn, decimal? amount = null, string? transactionId = null)
    {
        var confirmed = amount ?? world.Amount;

        return new ComgateTransferLine(
            1,
            transactionId ?? world.TransactionId,
            world.Number,
            "EUR",
            Comma(confirmed),
            Comma(confirmed - 0.35m),
            "0,35",
            $"{Iso(paidOn)} 12:00:00",
            null);
    }

    private static string Comma(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<ComgateOrder> PaidAsync(DateOnly paidOn, string environment = "Live")
    {
        var world = await StartedAsync(environment);
        await PayAsync(world);
        await PaidOnAsync(world, paidOn);

        return world;
    }

    private async Task PayAsync(ComgateOrder world, string status = "PAID")
    {
        _comgate.Says = new ComgateTransaction(
            world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Amount), "EUR", world.Number, world.Test);

        using var client = _withComgate.CreateClient();
        using var pushed = await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = world.MerchantId, transId = world.TransactionId },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
    }

    // Noon in Prague on the day: what the books will show, and a time no zone arithmetic moves to another day.
    private async Task PaidOnAsync(ComgateOrder world, DateOnly day)
    {
        var noon = day.ToDateTime(new TimeOnly(12, 0));
        var paidAt = new DateTimeOffset(noon, Prague.GetUtcOffset(noon)).ToUniversalTime();

        await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.payment_attempts
            SET started_at = {paidAt.AddMinutes(-10)}, changed_at = {paidAt}
            WHERE order_number = {world.Number} AND store_id = {world.Store.Store.StoreId}
            """,
            CancellationToken));
    }

    private async Task CancelAsync(ComgateOrder world)
    {
        using var cancelled = await world.Store.Admin.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}/cancel", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
    }

    private async Task<int> ImportAsync(ComgateOrder world)
    {
        await using var scope = TestStores.CreateScope(_withComgate.Services, world.Store.Store);

        return await scope.ServiceProvider.GetServices<IStoreCatchUp>().OfType<StatementImport>().Single().RunAsync(CancellationToken);
    }

    private async Task<Report> ReportAsync(ComgateOrder world, DateOnly from, DateOnly to)
    {
        using var owner = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId));

        return (await owner.GetFromJsonAsync<Report>(
            $"/api/admin/stores/{world.Store.Store.StoreId}/reconciliation?from={Iso(from)}&to={Iso(to)}", CancellationToken))!;
    }

    private async Task<ComgateOrder> StartedAsync(string environment = "Live", string? merchantId = null)
    {
        var merchant = merchantId ?? $"MERCHANT-{Guid.NewGuid():N}"[..20];
        var store = await FurnitureStore.CreateAsync(_factory);
        using var admin = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, store.Store.TenantId));

        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}",
            new { MerchantId = merchant, Environment = environment, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}/secret",
            new { Secret },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/payment-methods",
            new { Name = "Comgate", ProviderKey = ComgatePaymentProvider.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        using var shopper = new StorefrontApi(_withComgate, store.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));

        return new ComgateOrder(store, merchant, order.Number, attempt.Reference!, attempt.Amount, environment == "Test");
    }

    private sealed record ComgateOrder(FurnitureStore Store, string MerchantId, string Number, string TransactionId, decimal Amount, bool Test);

    private sealed record Report(
        string Provider,
        string TimeZone,
        DateOnly From,
        DateOnly To,
        DateOnly? StatementsThrough,
        int TestPayments,
        int Unexplained,
        List<ReportDay> Days,
        List<ReportDifference> Differences);

    private sealed record ReportDay(
        DateOnly Date, int Paid, decimal PaidAmount, int Settled, decimal SettledAmount, decimal Fees, decimal Transferred, int Unexplained);

    private sealed record ReportDifference(
        DateOnly Date,
        string Kind,
        bool Explained,
        string? OrderNumber,
        string? TransactionId,
        decimal? Expected,
        decimal? Settled,
        string? Currency,
        string Detail);
}
