using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// Nothing that would hurt in the wrong hands leaves the payment path through what it writes down: not the
// merchant's secret, not a card number, not a provider's whole answer — in a log line at any level, a trace
// attribute from any source, a metric tag, or an audit entry (Roadmap V2 §4.3, item 6; D-119, D-139, D-199). The
// real Comgate client runs over a stand-in for the network, so what the HTTP stack itself writes is in it too.
public sealed class PaymentTelemetryTests(ShopForgeApiFactory factory)
{
    private const string Merchant = "TELEMETRY-MERCHANT";
    private const string Secret = "comgate-secret-7f31c9d2";
    private const string TransactionId = "TELE-METR-Y001";
    private const string CardNumber = "4111111111111111";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // What a secret looks like once it is in a Basic header, which is the form an HTTP log would write it in.
    private static string BasicCredential => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Merchant}:{Secret}"));

    [Fact]
    public async Task A_payment_taken_and_given_back_writes_down_no_secret_no_card_and_no_whole_payload()
    {
        var logged = new EverythingLogged();
        var wire = new ComgateWire();
        var traced = new ConcurrentQueue<string>();
        var measured = new ConcurrentQueue<string>();
        var store = await FurnitureStore.CreateAsync(factory);

        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddFilter<EverythingLogged>(null, LogLevel.Trace));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISecretStore>(new InMemorySecrets());
                services.AddSingleton<ILoggerProvider>(logged);
                services.AddHttpClient<IComgatePayments, ComgateHttpPayments>().ConfigurePrimaryHttpMessageHandler(() => new ComgateOnTheWire(wire));
            });
        });

        using var traces = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => traced.Enqueue(Describe(activity)),
        };
        ActivitySource.AddActivityListener(traces);
        using var metrics = ListenToEveryMeter(measured);

        using var admin = await TestUsers.LoginAsync(host, await TestUsers.CreateAsync(factory.Services, store.Store.TenantId));
        await ConnectAsync(admin, store);

        using var shopper = new StorefrontApi(host, store.Store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 1 });
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        using var client = host.CreateClient();
        using var pushed = await client.PostAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new StringContent(wire.PushBody, Encoding.UTF8, "application/json"),
            CancellationToken);
        using var refunded = await admin.PostAsync($"/api/admin/stores/{store.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);

        var audited = await factory.QueryAsync(store.Store, async dbContext => await dbContext.Database.SqlQuery<string>(
                $"SELECT action || ' ' || subject || ' ' || coalesce(details, '') AS \"Value\" FROM auditing.audit_entries WHERE store_id = {store.Store.StoreId}")
            .ToListAsync(CancellationToken));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (added.StatusCode, pushed.StatusCode, refunded.StatusCode));
        // Created, asked about by the push, asked about again before the refund checks its currency, and refunded.
        Assert.Equal(["create", "status", "status", "refund"], wire.Calls);

        // Everything was being listened to — so a clean result is not a deaf one.
        Assert.Contains(logged.Lines, line => line.Contains(order.Number, StringComparison.Ordinal));
        Assert.Contains(traced, trace => trace.Contains(order.Number, StringComparison.Ordinal));
        Assert.Contains(measured, tag => tag.Contains("payment.provider=comgate", StringComparison.Ordinal));
        Assert.Contains(audited, entry => entry.StartsWith("order.refunded", StringComparison.Ordinal));

        string[] forbidden = [Secret, BasicCredential, CardNumber, wire.StatusPayload, wire.PushBody];

        foreach (var (kind, written) in new[] { ("log", logged.Lines), ("trace", traced), ("metric", measured), ("audit", (IEnumerable<string>)audited) })
        {
            foreach (var text in written)
            {
                Assert.False(forbidden.Any(secret => text.Contains(secret, StringComparison.Ordinal)), $"A {kind} carries what it must not: {text}");
            }
        }
    }

    private static async Task ConnectAsync(HttpClient admin, FurnitureStore store)
    {
        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}",
            new { MerchantId = Merchant, Environment = "Test", IsActive = true },
            CancellationToken);
        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}/secret",
            new { Secret },
            CancellationToken);
        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/payment-methods",
            new { Name = "Comgate", ProviderKey = ComgatePaymentProvider.ProviderKey, IsActive = true },
            CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created), (saved.StatusCode, kept.StatusCode, method.StatusCode));
    }

    private static string Describe(Activity activity)
    {
        var text = new StringBuilder($"{activity.Source.Name} {activity.DisplayName}");

        foreach (var (key, value) in activity.TagObjects)
        {
            text.Append($" {key}={value}");
        }

        foreach (var happened in activity.Events)
        {
            text.Append($" event={happened.Name}");

            foreach (var (key, value) in happened.Tags)
            {
                text.Append($" {key}={value}");
            }
        }

        return text.ToString();
    }

    private static MeterListener ListenToEveryMeter(ConcurrentQueue<string> measured)
    {
        var listener = new MeterListener { InstrumentPublished = (instrument, meterListener) => meterListener.EnableMeasurementEvents(instrument) };

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags, measured));
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) => Record(instrument, tags, measured));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags, measured));
        listener.Start();

        return listener;

        static void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags, ConcurrentQueue<string> measured)
        {
            foreach (var (key, value) in tags)
            {
                measured.Enqueue($"{instrument.Name} {key}={value}");
            }
        }
    }

    // Comgate as the real client meets it: the payment it was asked to create, its status when asked, the push it
    // sends, and the refund it takes. The status answer and the push carry a card number and everything else
    // Comgate puts in them, so that if any of it were written down, it would be found.
    private sealed class ComgateWire
    {
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyList<string> Calls => [.. _calls];

        public string ReferenceId { get; private set; } = string.Empty;

        public long Price { get; private set; }

        public string StatusPayload =>
            $$"""{"code":0,"message":"OK","test":"true","price":"{{Price}}","curr":"EUR","label":"Order","refId":"{{ReferenceId}}","method":"CARD_CZ_CSOB_2","email":"payer@example.test","phone":"+420777123456","name":"Jana Payer","transId":"{{TransactionId}}","status":"PAID","payerAcc":"CZ6508000000192000145399","cardNumber":"{{CardNumber}}"}""";

        public string PushBody =>
            $$"""{"merchant":"{{Merchant}}","transId":"{{TransactionId}}","test":"true","price":"{{Price}}","curr":"EUR","refId":"{{ReferenceId}}","status":"PAID","email":"payer@example.test","cardNumber":"{{CardNumber}}"}""";

        public void Called(string call) => _calls.Enqueue(call);

        public void Created(string referenceId, long price)
        {
            ReferenceId = referenceId;
            Price = price;
        }
    }

    private sealed class ComgateOnTheWire(ComgateWire wire) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path.EndsWith("/v2.0/payment.json", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                wire.Created(body["refId"]!.GetValue<string>(), body["price"]!.GetValue<long>());
                wire.Called("create");

                return Json(
                    $$"""{"code":0,"message":"OK","transId":"{{TransactionId}}","redirect":"https://payments.comgate.cz/client/instructions/index?id={{TransactionId}}"}""");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith($"/v2.0/payment/transId/{TransactionId}.json", StringComparison.Ordinal))
            {
                wire.Called("status");

                return Json(wire.StatusPayload);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/v2.0/refund.json", StringComparison.Ordinal))
            {
                wire.Called("refund");

                return Json("""{"code":0,"message":"OK"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    // Everything the host writes down, at every level: the message, every value it was given, every scope around it,
    // and an exception's whole text.
    private sealed class EverythingLogged : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Recorder(EverythingLogged owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = new StringBuilder($"{category}: {formatter(state, exception)}");

                Append(text, state);
                owner._scopes.ForEachScope((scope, builder) => Append(builder, scope), text);

                if (exception is not null)
                {
                    text.Append(' ').Append(exception);
                }

                owner._lines.Enqueue(text.ToString());
            }

            private static void Append(StringBuilder text, object? values)
            {
                if (values is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        text.Append($" {key}={value}");
                    }
                }
                else if (values is not null)
                {
                    text.Append($" {values}");
                }
            }
        }
    }
}
