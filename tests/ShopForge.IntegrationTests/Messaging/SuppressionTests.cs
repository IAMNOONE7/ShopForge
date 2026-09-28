using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Messaging;

public sealed class SuppressionTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The webhook is open to the internet: the signature is the only thing that makes it worth listening to.
    [Fact]
    public async Task An_event_anybody_could_have_posted_is_refused_and_suppresses_nobody()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var address = $"gone-{Guid.NewGuid():N}@example.test";

        using var response = await SendEventAsync(
            Payload("failed", address, furniture.Store.StoreId, signWith: "not-the-signing-key"));
        var listed = await SuppressedAsync(furniture);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(listed, suppressed => suppressed.Email == address);
    }

    [Fact]
    public async Task A_store_sees_the_addresses_it_may_not_write_to_and_can_let_one_back_in()
    {
        var furniture = await SigningStoreAsync();
        var bounced = $"gone-{Guid.NewGuid():N}@example.test";

        using var accepted = await SendEventAsync(Payload("failed", bounced, furniture.Store.StoreId));
        var listed = await SuppressedAsync(furniture);
        var entry = listed.Single(address => address.Email == bounced);

        using var allowed = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/suppressed-addresses/{entry.Id}", CancellationToken);
        var afterwards = await SuppressedAsync(furniture);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("Bounced", "mailbox does not exist"), (entry.Reason, entry.Detail));
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.DoesNotContain(afterwards, address => address.Email == bounced);
    }

    // Addresses are the most personal thing a store holds, so one store's must never appear in another's list.
    [Fact]
    public async Task One_store_never_sees_what_another_may_not_write_to()
    {
        var mine = await SigningStoreAsync();
        var theirs = await SigningStoreAsync();
        var bounced = $"gone-{Guid.NewGuid():N}@example.test";

        using var accepted = await SendEventAsync(Payload("failed", bounced, theirs.Store.StoreId));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains(await SuppressedAsync(theirs), address => address.Email == bounced);
        Assert.DoesNotContain(await SuppressedAsync(mine), address => address.Email == bounced);
    }

    [Fact]
    public async Task A_suppressed_address_is_not_written_to_again()
    {
        var furniture = await SigningStoreAsync();
        var email = $"gone-{Guid.NewGuid():N}@example.test";
        using var accepted = await SendEventAsync(Payload("failed", email, furniture.Store.StoreId));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null });

        // The message is finished rather than failed: nothing went wrong, we chose not to send it.
        for (var run = 0; run < 5; run++)
        {
            await factory.DispatchOutboxAsync(CancellationToken);
        }

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Empty(factory.Emails.For(email));
        Assert.Equal(0, await PendingForAsync(email));
    }

    private async Task<FurnitureStore> SigningStoreAsync() => await FurnitureStore.CreateAsync(factory);

    private async Task<HttpResponseMessage> SendEventAsync(string payload)
    {
        using var client = factory.CreateClient();

        return await client.PostAsync("/api/email/mailgun", new StringContent(payload, Encoding.UTF8, "application/json"), CancellationToken);
    }

    private async Task<List<SuppressedView>> SuppressedAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<SuppressedView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/suppressed-addresses", CancellationToken))!;

    private async Task<int> PendingForAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<DbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM messaging.outbox_messages WHERE status = 'Pending' AND payload LIKE {'%' + email + '%'}")
            .SingleAsync(CancellationToken);
    }

    private static string Payload(string eventName, string recipient, Guid? storeId, string? signWith = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var token = Guid.NewGuid().ToString("N");
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signWith ?? ShopForgeApiFactory.MailgunSigningKey),
            Encoding.UTF8.GetBytes(timestamp + token)));

        var data = new Dictionary<string, object?>
        {
            ["event"] = eventName,
            ["severity"] = "permanent",
            ["recipient"] = recipient,
            ["delivery-status"] = new { description = "mailbox does not exist" },
        };

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

    private sealed record SuppressedView(Guid Id, string Email, string Reason, string? Detail, DateTimeOffset SuppressedAt);
}
