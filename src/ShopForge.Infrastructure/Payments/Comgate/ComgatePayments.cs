using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure.Payments.Comgate;

// The one place that talks to Comgate, behind an interface so the provider can be exercised without a network.
//
// The field names below are the wire format and the only part of this integration that depends on Comgate's
// documentation being read correctly. They are kept together, and apart from everything else, so that checking
// them against the current API reference is one short file rather than a hunt. They were checked against the
// Merchant API reference at apidoc.comgate.cz on 2026-10-13, which found the original brief wrong in ten places
// (D-195), and the statement calls were written from it on 2026-10-14 (D-198).
internal interface IComgatePayments
{
    Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken);

    // What Comgate says the transaction is now. A push tells us to look; this is the looking, and it is the
    // only answer believed (D-141).
    Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken);

    // Giving money back, in the transaction's own currency, which is why no currency is sent. A refusal throws
    // RefundRefusedException; anything else that throws may have moved the money (D-194).
    Task RefundAsync(ComgateMerchant merchant, string transactionId, long amountInMinorUnits, bool test, CancellationToken cancellationToken);

    // The transfers Comgate paid the merchant on one day, a Prague day. Only paid, transferred production payments
    // are ever in one, so a test payment never is (D-198).
    Task<IReadOnlyList<long>> TransferIdsAsync(ComgateMerchant merchant, DateOnly day, CancellationToken cancellationToken);

    Task<IReadOnlyList<ComgateTransferLine>> TransferLinesAsync(ComgateMerchant merchant, long transferId, CancellationToken cancellationToken);
}

// Who the payment is taken for. The secret is read from the store's secret store at the moment of the call and
// goes no further than the Authorization header (D-139).
internal sealed record ComgateMerchant(string MerchantId, string Secret);

internal sealed record ComgatePayment(
    long PriceInMinorUnits,
    string Currency,
    string Label,
    string ReferenceId,
    string Email,
    string FullName,
    string? Phone,
    string Country,
    string Language,
    string Delivery,
    string Category,
    bool Test,
    string ExpirationTime,
    string ReturnUrl,
    string CancelUrl,
    string PendingUrl);

// What Comgate answers with: its own id for the transaction, and where to send the shopper. The URL is used
// exactly as given and never rebuilt.
internal sealed record ComgateCreated(string TransactionId, string RedirectUrl);

internal sealed record ComgateTransaction(
    string TransactionId,
    string Status,
    long PriceInMinorUnits,
    string Currency,
    string ReferenceId,
    bool Test);

// One line of a transfer as Comgate writes it, field for field. The amounts are text in crowns with a decimal comma
// and the type is a number the reference leaves unexplained, so turning a line into money is the reader's job, and a
// line nobody can read is reported rather than guessed at (D-198).
internal sealed record ComgateTransferLine(
    int? Type,
    string? TransactionId,
    string? ReferenceId,
    string? Currency,
    string? ConfirmedAmount,
    string? TransferredAmount,
    string? Fee,
    string? PaidAt,
    string? TransferredOn);

internal sealed class ComgateHttpPayments(HttpClient client) : IComgatePayments
{
    // The shopper chooses how to pay on Comgate's own page, from whatever the merchant has switched on there.
    private const string AnyMethod = "ALL";

    // Answers Comgate gives when it would not do what was asked, as against failing to: the request was wrong, the
    // transaction is cancelled, the amount is more than it allows. Nothing moved for any of them.
    private static readonly HashSet<int> Refusals = [1400, 1401, 1402];

    public async Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2.0/payment.json")
        {
            Content = JsonContent.Create(new CreateRequest(
                payment.PriceInMinorUnits,
                payment.Currency,
                payment.Label,
                payment.ReferenceId,
                AnyMethod,
                payment.Email,
                payment.Phone,
                payment.FullName,
                payment.Country,
                payment.Language,
                payment.Delivery,
                payment.Category,
                payment.Test,
                payment.ExpirationTime,
                payment.ReturnUrl,
                payment.CancelUrl,
                payment.PendingUrl)),
        };

        // Basic authentication with the merchant and its secret, as the security notes describe. Nothing about
        // the credentials is logged: the header is set here and read nowhere.
        request.Headers.Authorization = Basic(merchant);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateResponse>(cancellationToken);

        // Comgate answers 201 with a code of its own, and only 0 means a payment exists.
        return created is { Code: 0, TransactionId: { Length: > 0 } transactionId, RedirectUrl: { Length: > 0 } redirect }
            ? new ComgateCreated(transactionId, redirect)
            : throw new InvalidOperationException($"Comgate did not create the payment: {created?.Code} {created?.Message}");
    }

    public async Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2.0/payment/transId/{Uri.EscapeDataString(transactionId)}.json");
        request.Headers.Authorization = Basic(merchant);

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var found = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);

        // The status answer writes `test` as the text "true" or "false" and the price as text too, unlike the
        // request that created it; reading either as anything else failed every lookup there would ever be.
        return found is { Code: 0, TransactionId: { Length: > 0 } id, Status: { Length: > 0 } status }
            ? new ComgateTransaction(
                id,
                status,
                found.Price,
                found.Currency ?? string.Empty,
                found.ReferenceId ?? string.Empty,
                string.Equals(found.Test, "true", StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public async Task RefundAsync(
        ComgateMerchant merchant,
        string transactionId,
        long amountInMinorUnits,
        bool test,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2.0/refund.json")
        {
            Content = JsonContent.Create(new RefundRequest_(transactionId, amountInMinorUnits, test)),
        };

        request.Headers.Authorization = Basic(merchant);

        using var response = await client.SendAsync(request, cancellationToken);

        // Comgate says how a refund went in the body, under an HTTP 200 as often as not: reading only the status
        // line would take its refusal for a refund. A refusal it names is one; anything else it says, or a
        // server error, may have moved the money and is left to be checked (D-194).
        var answer = await AnswerAsync(response, cancellationToken);

        if (answer is { Code: 0 } && response.IsSuccessStatusCode)
        {
            return;
        }

        if (answer is { } refused && Refusals.Contains(refused.Code))
        {
            throw new RefundRefusedException($"Comgate refused the refund: {refused.Code} {refused.Message}");
        }

        throw new InvalidOperationException(
            $"Comgate answered the refund with {(int)response.StatusCode} {answer?.Code} {answer?.Message}; whether the money moved is not known.");
    }

    public async Task<IReadOnlyList<long>> TransferIdsAsync(ComgateMerchant merchant, DateOnly day, CancellationToken cancellationToken)
    {
        var transfers = await ListAsync<TransferResponse>(
            merchant, $"v2.0/transferList/date/{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json", cancellationToken);

        return [.. transfers.Select(transfer => transfer.TransferId)];
    }

    public async Task<IReadOnlyList<ComgateTransferLine>> TransferLinesAsync(ComgateMerchant merchant, long transferId, CancellationToken cancellationToken)
    {
        var lines = await ListAsync<TransferLineResponse>(
            merchant, $"v2.0/singleTransfer/transferId/{transferId.ToString(CultureInfo.InvariantCulture)}.json", cancellationToken);

        return [.. lines.Select(line => new ComgateTransferLine(
            line.Type,
            line.TransactionId,
            line.ReferenceId,
            line.Currency,
            line.ConfirmedAmount,
            line.TransferredAmount,
            line.Fee,
            line.PaidAt,
            line.TransferredOn))];
    }

    // Both statement calls answer an array, or 204 and nothing at all on a day with no transfers. Anything else is
    // a failure to read the day, and the day is read again rather than taken to have been empty.
    private async Task<List<T>> ListAsync<T>(ComgateMerchant merchant, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = Basic(merchant);

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
    }

    private static async Task<Answer?> AnswerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static AuthenticationHeaderValue Basic(ComgateMerchant merchant) => new(
        "Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{merchant.MerchantId}:{merchant.Secret}")));

    private sealed record TransactionResponse(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("transId")] string? TransactionId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("price"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long Price,
        [property: JsonPropertyName("curr")] string? Currency,
        [property: JsonPropertyName("refId")] string? ReferenceId,
        [property: JsonPropertyName("test")] string? Test);

    // No currency: a refund is in the currency of the transaction it gives back.
    private sealed record RefundRequest_(
        [property: JsonPropertyName("transId")] string TransactionId,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("test")] bool Test);

    private sealed record CreateRequest(
        [property: JsonPropertyName("price")] long Price,
        [property: JsonPropertyName("curr")] string Currency,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("refId")] string ReferenceId,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("phone"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Phone,
        [property: JsonPropertyName("fullName")] string FullName,
        [property: JsonPropertyName("country")] string Country,
        [property: JsonPropertyName("lang")] string Language,
        [property: JsonPropertyName("delivery")] string Delivery,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("test")] bool Test,
        [property: JsonPropertyName("expirationTime")] string ExpirationTime,
        [property: JsonPropertyName("url_paid")] string PaidUrl,
        [property: JsonPropertyName("url_cancelled")] string CancelledUrl,
        [property: JsonPropertyName("url_pending")] string PendingUrl);

    private sealed record CreateResponse(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("transId")] string? TransactionId,
        [property: JsonPropertyName("redirect")] string? RedirectUrl);

    private sealed record Answer(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message);

    private sealed record TransferResponse(
        [property: JsonPropertyName("transferId"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long TransferId);

    // The names are Comgate's, in Czech, exactly as its reference prints them.
    private sealed record TransferLineResponse(
        [property: JsonPropertyName("typ"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int? Type,
        [property: JsonPropertyName("ID Comgate")] string? TransactionId,
        [property: JsonPropertyName("ID od klienta")] string? ReferenceId,
        [property: JsonPropertyName("Měna")] string? Currency,
        [property: JsonPropertyName("Potvrzená částka")] string? ConfirmedAmount,
        [property: JsonPropertyName("Převedená částka")] string? TransferredAmount,
        [property: JsonPropertyName("Poplatek celkem")] string? Fee,
        [property: JsonPropertyName("Datum zaplacení")] string? PaidAt,
        [property: JsonPropertyName("Datum převodu")] string? TransferredOn);
}
