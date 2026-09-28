using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShopForge.Infrastructure.Email;

// Reads what Mailgun says happened to a message. Anybody can post here, so nothing is believed until the signature
// checks out against the signing key, and nothing old is believed at all (D-123).
public sealed class MailgunWebhook(EmailOptions options, TimeProvider clock)
{
    // Long enough for a retry from the provider, short enough that a captured request is not useful later.
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(15);

    public bool CanVerify => options.Mailgun.WebhookSigningKey.Length > 0;

    public MailgunEvent? Read(string body)
    {
        if (!CanVerify)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (!root.TryGetProperty("signature", out var signature) || !root.TryGetProperty("event-data", out var data))
        {
            return null;
        }

        if (!IsGenuine(signature))
        {
            return null;
        }

        var recipient = data.TryGetProperty("recipient", out var address) ? address.GetString() : null;

        if (recipient is not { Length: > 0 })
        {
            return null;
        }

        var reason = ReasonOf(data);

        return reason is null
            ? null
            : new MailgunEvent(recipient, reason.Value, StoreOf(data), DescriptionOf(data));
    }

    // "failed" covers a mailbox that is merely full, which is worth another try; only a permanent failure is.
    private static SuppressionReason? ReasonOf(JsonElement data) =>
        (data.TryGetProperty("event", out var name) ? name.GetString() : null) switch
        {
            "complained" => SuppressionReason.Complained,
            "permanent_fail" => SuppressionReason.Bounced,
            "failed" when data.TryGetProperty("severity", out var severity) && severity.GetString() == "permanent" => SuppressionReason.Bounced,
            _ => null,
        };

    // The message said which store it went out for, because the address alone does not say whose customer it is.
    private static Guid? StoreOf(JsonElement data) =>
        data.TryGetProperty("user-variables", out var variables)
            && variables.TryGetProperty(MailgunVariables.Store, out var store)
            && Guid.TryParse(store.GetString(), out var storeId)
                ? storeId
                : null;

    private static string? DescriptionOf(JsonElement data) =>
        data.TryGetProperty("delivery-status", out var status)
            && status.TryGetProperty("description", out var description)
            && description.GetString() is { Length: > 0 } text
                ? text
                : data.TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    private bool IsGenuine(JsonElement signature)
    {
        var timestamp = signature.TryGetProperty("timestamp", out var stamp) ? stamp.GetString() : null;
        var token = signature.TryGetProperty("token", out var value) ? value.GetString() : null;
        var provided = signature.TryGetProperty("signature", out var given) ? given.GetString() : null;

        if (timestamp is null || token is null || provided is null
            || !long.TryParse(timestamp, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);

        if (age > Tolerance || age < -Tolerance)
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.Mailgun.WebhookSigningKey),
            Encoding.UTF8.GetBytes(timestamp + token)));

        // Compared without leaking how much of it matched.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}

public sealed record MailgunEvent(string Recipient, SuppressionReason Reason, Guid? StoreId, string? Detail);

internal static class MailgunVariables
{
    public const string Store = "shopforge-store";
}
