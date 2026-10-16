using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace ShopForge.TestRig.Comgate;

// Comgate as ShopForge meets it on the wire (REST v2.0, D-195): Basic authentication with a merchant and its secret,
// a payment created with a page to send the shopper to, its status read back, refunds, and transfer statements. Each
// run has a merchant of its own, and what that merchant's gateway does is the run's to decide (D-203).
internal sealed class FakeComgate(RigOptions options, IHttpClientFactory clients, ILogger<FakeComgate> logger)
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ConcurrentDictionary<string, FakeMerchant> _merchants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FakeTransaction> _transactions = new(StringComparer.Ordinal);

    public void Register(string merchant, string secret) => _merchants[merchant] = new FakeMerchant(merchant, secret);

    public FakeMerchant? Merchant(string merchant) => _merchants.GetValueOrDefault(merchant);

    // The merchant whose credentials the request carries, or nobody: a wrong secret is refused as Comgate refuses it.
    public FakeMerchant? Authenticated(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return null;
        }

        string pair;

        try
        {
            pair = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return null;
        }

        var colon = pair.IndexOf(':', StringComparison.Ordinal);

        return colon > 0
            && _merchants.TryGetValue(pair[..colon], out var merchant)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pair[(colon + 1)..]), Encoding.UTF8.GetBytes(merchant.Secret))
                ? merchant
                : null;
    }

    public FakeTransaction Create(FakeMerchant merchant, PaymentRequest payment)
    {
        var transaction = new FakeTransaction(NewId(), merchant.Id, payment);
        _transactions[transaction.Id] = transaction;

        return transaction;
    }

    public FakeTransaction? Transaction(string id) => _transactions.GetValueOrDefault(id);

    public IEnumerable<FakeTransaction> TransactionsOf(string merchant) =>
        _transactions.Values.Where(transaction => transaction.Merchant == merchant).OrderBy(transaction => transaction.CreatedAt);

    public Uri PayPage(FakeTransaction transaction) => new(options.PublicOrigin, $"comgate/pay/{transaction.Id}");

    // What changed is pushed the way the merchant's gateway pushes: once, twice, late or not at all. A push names the
    // merchant and the transaction, and ShopForge asks what happened, so the status rides along only as Comgate's does.
    public async Task PushAsync(FakeTransaction transaction)
    {
        var behaviour = _merchants[transaction.Merchant].Behaviour;

        if (behaviour.Push == GatewayBehaviour.Drop)
        {
            transaction.Record(new PushRecord(DateTimeOffset.UtcNow, null, "dropped"));

            return;
        }

        if (behaviour.PushDelayMs > 0)
        {
            await Task.Delay(behaviour.PushDelayMs);
        }

        var times = behaviour.Push == GatewayBehaviour.Twice ? 2 : 1;

        for (var push = 0; push < times; push++)
        {
            try
            {
                using var client = clients.CreateClient();
                using var response = await client.PostAsJsonAsync(
                    new Uri(options.ApiOrigin, "api/payments/comgate"),
                    new { merchant = transaction.Merchant, transId = transaction.Id, status = transaction.Status, test = transaction.Test, refId = transaction.ReferenceId });
                transaction.Record(new PushRecord(DateTimeOffset.UtcNow, (int)response.StatusCode, "sent"));
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "The push for {TransactionId} did not reach the API.", transaction.Id);
                transaction.Record(new PushRecord(DateTimeOffset.UtcNow, null, "unreachable"));
            }
        }
    }

    private static string NewId()
    {
        Span<char> id = stackalloc char[14];

        for (var position = 0; position < id.Length; position++)
        {
            id[position] = position is 4 or 9 ? '-' : Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        }

        return new string(id);
    }
}

internal sealed class FakeMerchant(string id, string secret)
{
    public string Id { get; } = id;

    public string Secret { get; } = secret;

    public GatewayBehaviour Behaviour { get; set; } = GatewayBehaviour.Ordinary;
}

// What a merchant's gateway does, set per run. Every field has an ordinary answer, so a journey says only what it
// wants to be different.
internal sealed record GatewayBehaviour(
    string Push = GatewayBehaviour.Deliver,
    int PushDelayMs = 0,
    int StatusDelayMs = 0,
    bool StatusFails = false,
    string Refund = GatewayBehaviour.Accept,
    bool CreateFails = false)
{
    public const string Deliver = "deliver";
    public const string Drop = "drop";
    public const string Twice = "twice";
    public const string Accept = "accept";
    public const string Refuse = "refuse";
    public const string Fail = "fail";

    public static readonly GatewayBehaviour Ordinary = new();

    public bool IsValid =>
        Push is Deliver or Drop or Twice
        && Refund is Accept or Refuse or Fail
        && PushDelayMs is >= 0 and <= 60_000
        && StatusDelayMs is >= 0 and <= 120_000;
}

internal sealed class FakeTransaction(string id, string merchant, PaymentRequest payment)
{
    private readonly ConcurrentQueue<PushRecord> _pushes = new();
    private long _refunded;

    public string Id { get; } = id;

    public string Merchant { get; } = merchant;

    public PaymentRequest Payment { get; } = payment;

    public string ReferenceId => Payment.ReferenceId;

    public bool Test => Payment.Test;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    // Any word at all: PAID, CANCELLED, PENDING and AUTHORIZED are Comgate's, and anything else is what a journey
    // sets to see a status nobody wrote code for.
    public string Status { get; set; } = "PENDING";

    public long Refunded => Interlocked.Read(ref _refunded);

    public IReadOnlyCollection<PushRecord> Pushes => _pushes;

    public void Refund(long amount) => Interlocked.Add(ref _refunded, amount);

    public void Record(PushRecord push) => _pushes.Enqueue(push);
}

internal sealed record PushRecord(DateTimeOffset At, int? Answered, string What);

// The fields ShopForge sends to create a payment, as its client names them.
internal sealed record PaymentRequest(
    long Price,
    string Currency,
    string Label,
    string ReferenceId,
    string Email,
    bool Test,
    string PaidUrl,
    string CancelledUrl,
    string PendingUrl);
