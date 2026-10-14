using System.Globalization;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Comgate's transfer statements, read for the store in scope and put into the terms the reconciliation lays beside
// ShopForge's own payments (D-198). Only a live account is read, because Comgate settles production payments alone.
internal sealed class ComgateStatements(
    IComgatePayments payments,
    IProviderConnections connections,
    ISecretStore secrets) : ISettlementStatements
{
    // The one type the reference shows, on a payment. Refunds and chargebacks are not described at all, so any other
    // type is a line nobody has written code for, and it is reported as one.
    private const int PaymentType = 1;

    public string Key => ComgatePaymentProvider.ProviderKey;

    // "The date and transferDate parameters are interpreted in the Europe/Prague time zone."
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public async Task<IReadOnlyList<StatementLine>?> ReadDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        if (await LiveMerchantAsync(cancellationToken) is not { } merchant)
        {
            return null;
        }

        var lines = new List<StatementLine>();

        foreach (var transferId in await payments.TransferIdsAsync(merchant, day, cancellationToken))
        {
            foreach (var line in await payments.TransferLinesAsync(merchant, transferId, cancellationToken))
            {
                lines.Add(Read(transferId, line));
            }
        }

        return lines;
    }

    // What a line says, in ShopForge's terms. What cannot be read is named in the problem and left empty rather than
    // filled in: the transaction, the amount and the day are what a line is matched on. The fee and what was paid
    // out are only added up, so a line without them is still a line.
    internal static StatementLine Read(long transferId, ComgateTransferLine line)
    {
        var problems = new List<string>();
        var transactionId = Trimmed(line.TransactionId);
        var confirmed = Amount(line.ConfirmedAmount, "confirmed amount", required: true, problems);
        var fee = Amount(line.Fee, "fee", required: false, problems);
        var transferred = Amount(line.TransferredAmount, "transferred amount", required: false, problems);
        var paidOn = Day(line.PaidAt, "payment date", required: true, problems);
        var transferredOn = Day(line.TransferredOn, "transfer date", required: false, problems);

        if (transactionId is null)
        {
            problems.Add("no transaction");
        }

        return new StatementLine(
            transferId.ToString(CultureInfo.InvariantCulture),
            transferredOn,
            transactionId,
            Trimmed(line.ReferenceId),
            line.Type == PaymentType ? StatementLine.Payment : $"type {line.Type?.ToString(CultureInfo.InvariantCulture) ?? "none"}",
            Trimmed(line.Currency)?.ToUpperInvariant(),
            confirmed,
            fee,
            transferred,
            paidOn,
            problems.Count == 0 ? null : $"Comgate's line could not be read whole: {string.Join(", ", problems)}.");
    }

    // Crowns with a decimal comma — "10,00", "-0,15" — and a thousands separator allowed for, though the reference
    // shows none.
    private static decimal? Amount(string? text, string name, bool required, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required)
            {
                problems.Add($"no {name}");
            }

            return null;
        }

        var plain = text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        if (decimal.TryParse(plain, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        problems.Add($"the {name} \"{text}\"");

        return null;
    }

    // "2023-01-06 14:21:30" on a payment, "2023-01-10" on a transfer. Both are Prague times already, so the day is
    // the first ten characters.
    private static DateOnly? Day(string? text, string name, bool required, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required)
            {
                problems.Add($"no {name}");
            }

            return null;
        }

        if (text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        problems.Add($"the {name} \"{text}\"");

        return null;
    }

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<ComgateMerchant?> LiveMerchantAsync(CancellationToken cancellationToken) =>
        await connections.FindAsync(Key, cancellationToken) is { IsUsable: true, Environment: ProviderEnvironment.Live } connection
        && await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 } secret
            ? new ComgateMerchant(connection.MerchantId, secret)
            : null;
}
