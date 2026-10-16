using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using ShopForge.TestRig.Runs;

namespace ShopForge.TestRig.Comgate;

internal static class ComgateEndpoints
{
    public static void MapFakeComgate(this WebApplication app)
    {
        var wire = app.MapGroup("/comgate/v2.0");
        wire.MapPost("/payment.json", CreateAsync);
        wire.MapGet("/payment/transId/{transId}.json", StatusAsync);
        wire.MapPost("/refund.json", RefundAsync);

        // A test merchant is never paid out, so every statement day is empty, as Comgate's answers for one are.
        wire.MapGet("/transferList/date/{day}.json", (HttpRequest request, FakeComgate comgate) =>
            comgate.Authenticated(request) is null ? Refused() : Results.NoContent());
        wire.MapGet("/singleTransfer/transferId/{transferId}.json", (HttpRequest request, FakeComgate comgate) =>
            comgate.Authenticated(request) is null ? Refused() : Results.NoContent());

        app.MapGet("/comgate/pay/{transId}", PayPage);
        app.MapPost("/comgate/pay/{transId}", PayAsync);

        var control = app.MapGroup("/runs/{runId}/comgate");
        control.MapGet("/", Describe);
        control.MapPut("/behaviour", Behave);
        control.MapPost("/transactions/{transId}", SetStatusAsync);
        control.MapPost("/transactions/{transId}/push", PushAgainAsync);
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, FakeComgate comgate)
    {
        if (comgate.Authenticated(request) is not { } merchant)
        {
            return Refused();
        }

        if (merchant.Behaviour.CreateFails)
        {
            return Results.Json(new Answer(1500, "The gateway stand-in was told to fail."), statusCode: 500);
        }

        if (await request.ReadFromJsonAsync<CreateBody>() is not { Price: > 0, Currency.Length: > 0, ReferenceId.Length: > 0 } body)
        {
            return Results.Json(new Answer(1400, "A payment needs a price, a currency and a reference."), statusCode: 400);
        }

        var transaction = comgate.Create(merchant, new PaymentRequest(
            body.Price, body.Currency, body.Label ?? string.Empty, body.ReferenceId, body.Email ?? string.Empty, body.Test,
            body.PaidUrl ?? string.Empty, body.CancelledUrl ?? string.Empty, body.PendingUrl ?? string.Empty));

        return Results.Json(new Created(0, "OK", transaction.Id, comgate.PayPage(transaction).ToString()), statusCode: 201);
    }

    private static async Task<IResult> StatusAsync(string transId, HttpRequest request, FakeComgate comgate)
    {
        if (comgate.Authenticated(request) is not { } merchant)
        {
            return Refused();
        }

        if (merchant.Behaviour.StatusDelayMs > 0)
        {
            await Task.Delay(merchant.Behaviour.StatusDelayMs, request.HttpContext.RequestAborted);
        }

        if (merchant.Behaviour.StatusFails)
        {
            return Results.Json(new Answer(1500, "The gateway stand-in was told to fail."), statusCode: 500);
        }

        if (comgate.Transaction(transId) is not { } transaction || transaction.Merchant != merchant.Id)
        {
            return Results.Json(new Answer(1400, "No such payment."), statusCode: 404);
        }

        // As Comgate writes it: the price and the test flag as text.
        return Results.Json(new Status(
            0,
            "OK",
            transaction.Merchant,
            transaction.Test ? "true" : "false",
            transaction.Payment.Price.ToString(CultureInfo.InvariantCulture),
            transaction.Payment.Currency,
            transaction.Payment.Label,
            transaction.ReferenceId,
            transaction.Payment.Email,
            transaction.Id,
            transaction.Status));
    }

    private static async Task<IResult> RefundAsync(HttpRequest request, FakeComgate comgate)
    {
        if (comgate.Authenticated(request) is not { } merchant)
        {
            return Refused();
        }

        if (await request.ReadFromJsonAsync<RefundBody>() is not { TransactionId.Length: > 0, Amount: > 0 } body
            || comgate.Transaction(body.TransactionId) is not { } transaction
            || transaction.Merchant != merchant.Id)
        {
            return Results.Json(new Answer(1400, "No such payment to refund."));
        }

        // Comgate says how a refund went in the body, under a 200 as often as not (D-194).
        switch (merchant.Behaviour.Refund)
        {
            case GatewayBehaviour.Refuse:
                return Results.Json(new Answer(1401, "The gateway stand-in was told to refuse the refund."));
            case GatewayBehaviour.Fail:
                return Results.Json(new Answer(1500, "The gateway stand-in was told to fail."), statusCode: 500);
        }

        if (transaction.Refunded + body.Amount > transaction.Payment.Price)
        {
            return Results.Json(new Answer(1402, "More than was paid."));
        }

        transaction.Refund(body.Amount);

        return Results.Json(new Answer(0, "OK"));
    }

    // The page Comgate sends the shopper to, reduced to the three ways it can end.
    private static IResult PayPage(string transId, FakeComgate comgate)
    {
        if (comgate.Transaction(transId) is not { } transaction)
        {
            return Results.NotFound();
        }

        var html = HtmlEncoder.Default;
        var amount = (transaction.Payment.Price / 100m).ToString("0.00", CultureInfo.InvariantCulture);

        return Results.Content(
            $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Comgate stand-in</title></head>
            <body>
            <main>
            <h1>Comgate stand-in</h1>
            <p>{html.Encode(transaction.Payment.Label)}: <strong>{amount} {html.Encode(transaction.Payment.Currency)}</strong>, order {html.Encode(transaction.ReferenceId)}</p>
            <form method="post">
            <button type="submit" name="outcome" value="paid">Pay</button>
            <button type="submit" name="outcome" value="cancelled">Decline</button>
            <button type="submit" name="outcome" value="pending">Leave pending</button>
            </form>
            </main>
            </body>
            </html>
            """,
            "text/html; charset=utf-8");
    }

    // The shopper's choice becomes the transaction's status, the push goes as the merchant's gateway sends it, and the
    // shopper is sent back to wherever ShopForge asked. A late push is not waited for, as a real one would not be.
    private static async Task<IResult> PayAsync(string transId, HttpRequest request, FakeComgate comgate)
    {
        if (comgate.Transaction(transId) is not { } transaction || !request.HasFormContentType)
        {
            return Results.NotFound();
        }

        var outcome = (await request.ReadFormAsync())["outcome"].ToString();
        (string Status, string Back)? chosen = outcome switch
        {
            "paid" => ("PAID", transaction.Payment.PaidUrl),
            "cancelled" => ("CANCELLED", transaction.Payment.CancelledUrl),
            "pending" => ("PENDING", transaction.Payment.PendingUrl),
            _ => null,
        };

        if (chosen is not { } choice)
        {
            return Results.BadRequest();
        }

        transaction.Status = choice.Status;
        var push = comgate.PushAsync(transaction);

        if (comgate.Merchant(transaction.Merchant)!.Behaviour.PushDelayMs == 0)
        {
            await push;
        }

        return Results.Redirect(choice.Back, permanent: false, preserveMethod: false);
    }

    private static Results<Ok<GatewayView>, NotFound> Describe(string runId, RunBook runs, FakeComgate comgate) =>
        MerchantOf(runId, runs, comgate) is { } merchant
            ? TypedResults.Ok(new GatewayView(
                merchant.Id,
                merchant.Behaviour,
                [.. comgate.TransactionsOf(merchant.Id).Select(TransactionView.From)]))
            : TypedResults.NotFound();

    private static Results<Ok<GatewayBehaviour>, NotFound, ValidationProblem> Behave(
        string runId, GatewayBehaviour behaviour, RunBook runs, FakeComgate comgate)
    {
        if (MerchantOf(runId, runs, comgate) is not { } merchant)
        {
            return TypedResults.NotFound();
        }

        if (!behaviour.IsValid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["behaviour"] = ["push is deliver, drop or twice; refund is accept, refuse or fail; delays are at most a minute, two for a status."],
            });
        }

        merchant.Behaviour = behaviour;

        return TypedResults.Ok(behaviour);
    }

    // Any status, PAID to one nobody has heard of, with the push or without it.
    private static async Task<Results<Ok<TransactionView>, NotFound, ValidationProblem>> SetStatusAsync(
        string runId, string transId, StatusChange change, RunBook runs, FakeComgate comgate)
    {
        if (MerchantOf(runId, runs, comgate) is not { } merchant
            || comgate.Transaction(transId) is not { } transaction
            || transaction.Merchant != merchant.Id)
        {
            return TypedResults.NotFound();
        }

        if (change.Status is not { Length: > 0 and <= 40 } status)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["A status is a word of up to forty characters."] });
        }

        transaction.Status = status.ToUpperInvariant();

        if (change.Push)
        {
            await comgate.PushAsync(transaction);
        }

        return TypedResults.Ok(TransactionView.From(transaction));
    }

    private static async Task<Results<Ok<TransactionView>, NotFound>> PushAgainAsync(
        string runId, string transId, RunBook runs, FakeComgate comgate)
    {
        if (MerchantOf(runId, runs, comgate) is not { } merchant
            || comgate.Transaction(transId) is not { } transaction
            || transaction.Merchant != merchant.Id)
        {
            return TypedResults.NotFound();
        }

        await comgate.PushAsync(transaction);

        return TypedResults.Ok(TransactionView.From(transaction));
    }

    private static FakeMerchant? MerchantOf(string runId, RunBook runs, FakeComgate comgate) =>
        runs.Find(runId) is { } run ? comgate.Merchant(run.Comgate.Merchant) : null;

    private static IResult Refused() => Results.Json(new Answer(1401, "Unauthorized."), statusCode: (int)HttpStatusCode.Unauthorized);

    private sealed record CreateBody(
        [property: JsonPropertyName("price")] long Price,
        [property: JsonPropertyName("curr")] string? Currency,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("refId")] string? ReferenceId,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("test")] bool Test,
        [property: JsonPropertyName("url_paid")] string? PaidUrl,
        [property: JsonPropertyName("url_cancelled")] string? CancelledUrl,
        [property: JsonPropertyName("url_pending")] string? PendingUrl);

    private sealed record RefundBody(
        [property: JsonPropertyName("transId")] string? TransactionId,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("test")] bool Test);

    private sealed record Answer(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);

    private sealed record Created(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("transId")] string TransactionId,
        [property: JsonPropertyName("redirect")] string Redirect);

    private sealed record Status(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("merchant")] string Merchant,
        [property: JsonPropertyName("test")] string Test,
        [property: JsonPropertyName("price")] string Price,
        [property: JsonPropertyName("curr")] string Currency,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("refId")] string ReferenceId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("transId")] string TransactionId,
        [property: JsonPropertyName("status")] string State);
}

internal sealed record StatusChange(string? Status, bool Push = true);

internal sealed record GatewayView(string Merchant, GatewayBehaviour Behaviour, List<TransactionView> Transactions);

internal sealed record TransactionView(
    string Id, string ReferenceId, string Status, long Price, string Currency, bool Test, long Refunded, List<PushRecord> Pushes)
{
    public static TransactionView From(FakeTransaction transaction) =>
        new(
            transaction.Id,
            transaction.ReferenceId,
            transaction.Status,
            transaction.Payment.Price,
            transaction.Payment.Currency,
            transaction.Test,
            transaction.Refunded,
            [.. transaction.Pushes]);
}
