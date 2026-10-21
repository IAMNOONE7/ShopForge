using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.IntegrationTests.Payments;
using ShopForge.IntegrationTests.Provisioning;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Email;

// The application sending through a recorded Mailgun, with a vault and a clock of the test's own (D-211). Its outbox
// worker is held back for a day: the outbox is the whole suite's, and this host would otherwise deliver other tests'
// letters to a Mailgun they know nothing about. Each test hands its messages to the mail handler itself, inside the
// store they belong to.
internal sealed class MailHost : IAsyncDisposable
{
    public const string PlatformDomain = "mg.platform.test";
    public const string PlatformSender = "no-reply@mg.platform.test";

    private MailHost(WebApplicationFactory<Program> host, RecordedMailgun mailgun, InMemorySecrets secrets, TestClock clock)
    {
        Host = host;
        Mailgun = mailgun;
        Secrets = secrets;
        Clock = clock;
    }

    public WebApplicationFactory<Program> Host { get; }

    public RecordedMailgun Mailgun { get; }

    public InMemorySecrets Secrets { get; }

    public TestClock Clock { get; }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static async Task<MailHost> StartAsync(ShopForgeApiFactory factory, bool withVault = true)
    {
        var mailgun = await RecordedMailgun.StartAsync();
        var secrets = new InMemorySecrets();
        var clock = new TestClock();
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:Provider", "mailgun");
            builder.UseSetting("Email:SenderAddress", PlatformSender);
            builder.UseSetting("Email:Mailgun:ApiKey", RecordedMailgun.AccountKey);
            builder.UseSetting("Email:Mailgun:Domain", PlatformDomain);
            builder.UseSetting("Email:Mailgun:BaseUrl", mailgun.BaseUrl);
            builder.UseSetting("Outbox:IntervalSeconds", "86400");
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<IEmailDelivery, MailgunEmailDelivery>((provider, client) =>
                    MailgunEmailDelivery.Configure(client, provider.GetRequiredService<EmailOptions>()));
                services.AddSingleton<TimeProvider>(clock);

                if (withVault)
                {
                    services.AddSingleton<ISecretStore>(secrets);
                }
            });
        });

        return new MailHost(host, mailgun, secrets, clock);
    }

    // A store with a domain of its own, proved the way a merchant proves one, and its owner signed in.
    public async Task<ProvedStore> StoreWithProvedDomainAsync(ShopForgeApiFactory factory, string? domain = null)
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(Host.Services);
        var owner = await TestUsers.LoginAsync(Host, await TestUsers.CreateAsync(Host.Services, store.TenantId));

        return new ProvedStore(store, owner, await ProveAsync(factory, store, owner, domain ?? $"{TestStores.UniqueLabel()}.example.test"));
    }

    public static async Task<string> ProveAsync(ShopForgeApiFactory factory, TestStore store, HttpClient owner, string domain)
    {
        using var added = await owner.PostAsJsonAsync($"/api/admin/stores/{store.StoreId}/domains", new { HostName = domain }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var claim = (await added.Content.ReadFromJsonAsync<Claim>(CancellationToken))!;
        factory.Dns.Publish(claim.ChallengeName!, claim.ChallengeValue!);

        using var verified = await owner.PostAsync($"/api/admin/stores/{store.StoreId}/domains/{claim.Id}/verify", content: null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return domain;
    }

    public static async Task<HttpResponseMessage> ConnectAsync(ProvedStore proved, string domain, string sender, string? replyTo = null) =>
        await proved.Owner.PostAsJsonAsync(
            $"/api/admin/stores/{proved.Store.StoreId}/email/domain", new { Domain = domain, SenderAddress = sender, ReplyTo = replyTo }, CancellationToken);

    public static async Task<Guid> StartedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("operationId").GetGuid();
    }

    // Connects mail.<proved domain>, mail from obchod@<proved domain>, and runs the operation as far as it goes before
    // the records are published.
    public async Task<Guid> ConnectAndWaitAsync(ProvedStore proved, string? replyTo = null)
    {
        var operationId = await StartedAsync(await ConnectAsync(proved, $"mail.{proved.Domain}", $"obchod@{proved.Domain}", replyTo));
        await RunAsync(operationId);

        return operationId;
    }

    // The records published at the merchant's DNS host and Mailgun's next look, after which the store sends from it.
    public async Task<OperationDetail> PublishAndFinishAsync(ProvedStore proved, Guid operationId)
    {
        Mailgun.Publish($"mail.{proved.Domain}");
        Clock.Advance(TimeSpan.FromHours(1));
        await RunAsync(operationId);

        return await OperationAsync(proved.Store, operationId);
    }

    public Task<bool> RunAsync(Guid operationId) =>
        Host.Services.GetRequiredService<OperationRunner>().RunAsync(operationId, CancellationToken);

    public async Task<OperationDetail> RunToRestAsync(TestStore store, Guid operationId)
    {
        for (var round = 0; round < 20; round++)
        {
            await RunAsync(operationId);
            var operation = await OperationAsync(store, operationId);

            if (operation.State is not (OperationState.Running or OperationState.Cancelling))
            {
                return operation;
            }

            Clock.Advance(TimeSpan.FromHours(1));
        }

        throw new InvalidOperationException("The operation did not come to rest.");
    }

    public async Task<OperationDetail> OperationAsync(TestStore store, Guid operationId)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return (await scope.ServiceProvider.GetRequiredService<OperationAdmin>().FindAsync(operationId, CancellationToken))!;
    }

    public static async Task<JsonElement> ViewAsync(ProvedStore proved) =>
        JsonDocument.Parse(await proved.Owner.GetStringAsync($"/api/admin/stores/{proved.Store.StoreId}/email", CancellationToken)).RootElement;

    // A message handed to the mail handler inside the store it belongs to, or none, as the outbox would hand it.
    public async Task SendAsync(TestStore? store, string to)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        await scope.ServiceProvider.GetRequiredService<IEventHandler<EmailRequested>>()
            .HandleAsync(new EmailRequested(to, "Your order", "Thank you."), CancellationToken);
    }

    // A permanent failure Mailgun reports for a message that went out for the store, signed as Mailgun signs it, at the
    // host's own time.
    public async Task<HttpResponseMessage> BounceAsync(Guid storeId, string recipient)
    {
        var timestamp = Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var token = Guid.NewGuid().ToString("N");
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(ShopForgeApiFactory.MailgunSigningKey), Encoding.UTF8.GetBytes(timestamp + token)));
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["signature"] = new { timestamp, token, signature },
            ["event-data"] = new Dictionary<string, object?>
            {
                ["event"] = "failed",
                ["severity"] = "permanent",
                ["recipient"] = recipient,
                ["delivery-status"] = new { description = "mailbox does not exist" },
                ["user-variables"] = new Dictionary<string, string> { ["shopforge-store"] = storeId.ToString() },
            },
        });
        using var client = Host.CreateClient();

        return await client.PostAsync("/api/email/mailgun", new StringContent(payload, Encoding.UTF8, "application/json"), CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Mailgun.DisposeAsync();
    }

    private sealed record Claim(Guid Id, string? ChallengeName, string? ChallengeValue);
}

internal sealed record ProvedStore(TestStore Store, HttpClient Owner, string Domain);
