using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShopForge.Infrastructure.Email;

namespace ShopForge.UnitTests.Email;

public sealed class MailgunWebhookTests
{
    private const string SigningKey = "signing-key-123";

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_permanent_failure_suppresses_the_address_for_the_store_it_was_sent_for()
    {
        var store = Guid.CreateVersion7();
        var delivery = Read(Payload("failed", severity: "permanent", storeId: store));

        Assert.NotNull(delivery);
        Assert.Equal(("buyer@example.test", SuppressionReason.Bounced, (Guid?)store), (delivery.Recipient, delivery.Reason, delivery.StoreId));
        Assert.Equal("mailbox does not exist", delivery.Detail);
    }

    [Fact]
    public void A_complaint_suppresses_the_address_too()
    {
        var delivery = Read(Payload("complained", severity: null, storeId: Guid.CreateVersion7()));

        Assert.Equal(SuppressionReason.Complained, delivery!.Reason);
    }

    // A full mailbox is worth trying again; only a permanent failure ends the conversation.
    [Fact]
    public void A_temporary_failure_changes_nothing()
    {
        Assert.Null(Read(Payload("failed", severity: "temporary", storeId: Guid.CreateVersion7())));
    }

    [Fact]
    public void A_delivery_that_simply_worked_changes_nothing()
    {
        Assert.Null(Read(Payload("delivered", severity: null, storeId: Guid.CreateVersion7())));
    }

    // Anybody can post to a webhook, so the only thing that makes it true is the signature.
    [Fact]
    public void An_event_signed_with_the_wrong_key_is_not_believed()
    {
        var payload = Payload("complained", severity: null, storeId: null, signWith: "somebody-elses-key");

        Assert.Null(Read(payload));
    }

    [Fact]
    public void An_event_signed_long_ago_is_not_believed()
    {
        var payload = Payload("complained", severity: null, storeId: null, at: Now.AddHours(-2));

        Assert.Null(Read(payload));
    }

    [Fact]
    public void Nothing_is_believed_at_all_without_a_signing_key()
    {
        var webhook = new MailgunWebhook(new EmailOptions(), new FixedClock(Now));

        Assert.False(webhook.CanVerify);
        Assert.Null(webhook.Read(Payload("complained", severity: null, storeId: null)));
    }

    // Mail that belongs to no store bounces too, and its suppression belongs to nobody (D-111).
    [Fact]
    public void An_event_for_mail_with_no_store_names_none()
    {
        var delivery = Read(Payload("complained", severity: null, storeId: null));

        Assert.Null(delivery!.StoreId);
    }

    private static MailgunEvent? Read(string payload) =>
        new MailgunWebhook(
            new EmailOptions { Mailgun = new MailgunOptions { WebhookSigningKey = SigningKey } },
            new FixedClock(Now)).Read(payload);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Payload(string eventName, string? severity, Guid? storeId, string? signWith = null, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? Now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        const string token = "token-abc";
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signWith ?? SigningKey),
            Encoding.UTF8.GetBytes(timestamp + token)));

        var data = new Dictionary<string, object?>
        {
            ["event"] = eventName,
            ["recipient"] = "buyer@example.test",
            ["delivery-status"] = new { description = "mailbox does not exist" },
        };

        if (severity is not null)
        {
            data["severity"] = severity;
        }

        if (storeId is { } store)
        {
            data["user-variables"] = new Dictionary<string, string> { ["shopforge-store"] = store.ToString() };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["signature"] = new { timestamp, token, signature },
            ["event-data"] = data,
        });
    }
}
