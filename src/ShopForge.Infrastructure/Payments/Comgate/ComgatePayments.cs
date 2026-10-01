using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace ShopForge.Infrastructure.Payments.Comgate;

// The one place that talks to Comgate, behind an interface so the provider can be exercised without a network.
//
// The field names below are the wire format and the only part of this integration that depends on Comgate's
// documentation being read correctly. They are kept together, and apart from everything else, so that checking
// them against the current API reference is one short file rather than a hunt.
internal interface IComgatePayments
{
    Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken);

    // What Comgate says the transaction is now. A push tells us to look; this is the looking, and it is the
    // only answer believed (D-141).
    Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken);
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
    string? FullName,
    string Country,
    string Language,
    string Delivery,
    string Category,
    bool Test,
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

internal sealed class ComgateHttpPayments(HttpClient client) : IComgatePayments
{
    public async Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2.0/payment.json")
        {
            Content = JsonContent.Create(new CreateRequest(
                payment.PriceInMinorUnits,
                payment.Currency,
                payment.Label,
                payment.ReferenceId,
                new Payer(payment.Email, payment.FullName),
                payment.Country,
                payment.Language,
                payment.Delivery,
                payment.Category,
                payment.Test,
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

        return created is { TransactionId: { Length: > 0 } transactionId, RedirectUrl: { Length: > 0 } redirect }
            ? new ComgateCreated(transactionId, redirect)
            : throw new InvalidOperationException("Comgate created a payment without a transaction id or a redirect.");
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

        return found is { TransactionId: { Length: > 0 } id, Status: { Length: > 0 } status }
            ? new ComgateTransaction(id, status, found.Price, found.Currency ?? string.Empty, found.ReferenceId ?? string.Empty, found.Test)
            : null;
    }

    private static AuthenticationHeaderValue Basic(ComgateMerchant merchant) => new(
        "Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{merchant.MerchantId}:{merchant.Secret}")));

    private sealed record TransactionResponse(
        [property: JsonPropertyName("transId")] string? TransactionId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("price")] long Price,
        [property: JsonPropertyName("curr")] string? Currency,
        [property: JsonPropertyName("refId")] string? ReferenceId,
        [property: JsonPropertyName("test")] bool Test);

    private sealed record CreateRequest(
        [property: JsonPropertyName("price")] long Price,
        [property: JsonPropertyName("curr")] string Currency,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("refId")] string ReferenceId,
        [property: JsonPropertyName("payer")] Payer Payer,
        [property: JsonPropertyName("country")] string Country,
        [property: JsonPropertyName("lang")] string Language,
        [property: JsonPropertyName("delivery")] string Delivery,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("test")] bool Test,
        [property: JsonPropertyName("returnUrl")] string ReturnUrl,
        [property: JsonPropertyName("cancelUrl")] string CancelUrl,
        [property: JsonPropertyName("pendingUrl")] string PendingUrl);

    private sealed record Payer(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record CreateResponse(
        [property: JsonPropertyName("transId")] string? TransactionId,
        [property: JsonPropertyName("redirect")] string? RedirectUrl);
}
