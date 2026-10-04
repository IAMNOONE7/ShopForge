using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Persistence;
using ShopForge.IntegrationTests;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Email;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ShopForgeApiFactory))]

namespace ShopForge.IntegrationTests;

public sealed class ShopForgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public ShopForgeApiFactory() => EmailDelivery = new RecordingEmailDelivery(Emails);

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly AzuriteContainer _fileStorage = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    // Real mail still goes to the recording delivery; the key only makes the webhook believable.
    public const string MailgunSigningKey = "test-signing-key";

    // Read once, after the containers are up, and used for every host built afterwards. Asking Testcontainers
    // again each time a host is built is what put `127.0.0.1:1` — its answer for a container with no published
    // port — into a connection string in the middle of a run, which failed one test in roughly every third
    // full suite with a transient "connection refused".
    private string? _databaseConnectionString;
    private string? _fileStorageConnectionString;

    // The suite's own PostgreSQL. A test that needs a database of its own makes one on this server rather than
    // starting a second container.
    internal string DatabaseConnectionString => Started(_databaseConnectionString);

    public RecordedEmails Emails { get; } = new();

    internal RecordingEmailDelivery EmailDelivery { get; }

    // Real DNS is not something a test can arrange, so the challenge answers come from here.
    internal FakeDnsTxtRecords Dns { get; } = new();

    // Tests do not wait for the worker's ten-second tick; they run the outbox when they need what it delivers.
    internal QueryCounter Queries { get; } = new();

    public Task<int> DispatchOutboxAsync(CancellationToken cancellationToken = default) =>
        Services.GetRequiredService<OutboxDispatcher>().DispatchAsync(cancellationToken);

    // Delivery is asynchronous and every test shares one worker, so a message can be leased by someone else's run —
    // for up to five minutes, in a suite that runs in parallel. A test therefore waits for the effect it needs
    // instead of assuming a single dispatch produced it, and waits long enough to outlast a busy moment.
    public async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> until, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await DispatchOutboxAsync(cancellationToken);
            var value = await read();

            if (until(value))
            {
                return value;
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new InvalidOperationException("The outbox did not produce what the test was waiting for.");
    }

    internal async Task<T> QueryAsync<T>(TestStore store, Func<DbContext, Task<T>> query)
    {
        await using var scope = TestStores.CreateScope(Services, store);

        return await query(scope.ServiceProvider.GetRequiredService<DbContext>());
    }

    // A host built before the containers are up has nowhere to connect to, and saying so is better than
    // handing out an address that cannot work.
    private static string Started(string? connectionString) =>
        connectionString ?? throw new InvalidOperationException("The suite's containers have not started yet.");

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _fileStorage.StartAsync());
        _databaseConnectionString = _database.GetConnectionString();
        _fileStorageConnectionString = _fileStorage.GetConnectionString();
        await Services.MigrateDatabaseAsync();
        await Services.CreateFileStorageContainerAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ShopForge", Started(_databaseConnectionString));
        builder.UseSetting("ConnectionStrings:FileStorage", Started(_fileStorageConnectionString));

        // The suite is one caller doing in a minute what a crowd would do in a day, so every window is opened
        // wide here; each limit is exercised by a test of its own that closes the one it cares about.
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:Writes:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Expensive:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Global:PermitLimit", "1000000");

        // The test host speaks plain HTTP, so cookies cannot be marked Secure the way a deployment marks them.
        builder.UseSetting("Security:RequireSecureCookies", "false");
        builder.UseSetting("Email:Mailgun:WebhookSigningKey", MailgunSigningKey);
        // Only the last hop is faked: registration still writes an outbox message, which the dispatcher delivers.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailDelivery>(EmailDelivery);
            services.AddSingleton<IDnsTxtRecords>(Dns);
            services.ConfigureDbContext<ShopForgeDbContext>(options => options.AddInterceptors(Queries));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        await _fileStorage.DisposeAsync();
    }
}

// Stands in for the world's DNS: a test says what a name answers, and nothing leaves the machine.
internal sealed class FakeDnsTxtRecords : IDnsTxtRecords
{
    private readonly ConcurrentDictionary<string, string[]> _records = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string name, params string[] values) => _records[name] = values;

    public Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_records.TryGetValue(name, out var values) ? values : []);
}

// Messages normally go to the log (D-053); tests read them here to follow the links a customer would click.
public sealed class RecordedEmails
{
    private readonly ConcurrentQueue<DeliveredEmail> _messages = new();
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    public void Add(EmailMessage message, IReadOnlyList<EmailAttachment> attachments) =>
        _messages.Enqueue(new DeliveredEmail(message, attachments));

    public IReadOnlyList<EmailMessage> For(string recipient) =>
        [.. _messages.Where(delivered => delivered.Message.To == recipient).Select(delivered => delivered.Message)];

    public IReadOnlyList<EmailAttachment> AttachmentsFor(string recipient, string subjectContains) =>
        _messages
            .Where(delivered => delivered.Message.To == recipient
                && delivered.Message.Subject.Contains(subjectContains, StringComparison.Ordinal))
            .SelectMany(delivered => delivered.Attachments)
            .ToList();

    // The newest message that actually carries a link, not the newest message. Plenty of what a shop sends has no
    // link in it — an order confirmation, "you already have an account" — and reading only the last one made a
    // test wait for a link that had already arrived.
    public string? LatestLinkFor(string recipient) =>
        For(recipient).Select(TokenIn).OfType<string>().LastOrDefault();

    // The next link this address has been sent that no test has taken yet. Links are spent when they are used,
    // two of them can be in flight at once, and the newest delivered is not always the newest issued — so asking
    // for "the latest" hands back a spent one often enough to have broken four tests in different weeks.
    public string? NextLinkFor(string recipient)
    {
        lock (_taken)
        {
            var link = For(recipient)
                .Select(TokenIn)
                .OfType<string>()
                .FirstOrDefault(candidate => !_taken.Contains(candidate));

            if (link is not null)
            {
                _taken.Add(link);
            }

            return link;
        }
    }

    private sealed record DeliveredEmail(EmailMessage Message, IReadOnlyList<EmailAttachment> Attachments);

    private static string? TokenIn(EmailMessage message) =>
        message.Body.Split("token=").ElementAtOrDefault(1)?.Split(' ')[0].TrimEnd('.');
}

// Tests run side by side against one host, so a test that needs delivery to fail says for whom.
internal sealed class RecordingEmailDelivery(RecordedEmails recorded) : IEmailDelivery
{
    private readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.OrdinalIgnoreCase);

    public void FailFor(string recipient) => _failing[recipient] = 0;

    public void StopFailingFor(string recipient) => _failing.TryRemove(recipient, out _);

    public Task DeliverAsync(EmailMessage message, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken)
    {
        if (_failing.ContainsKey(message.To))
        {
            return Task.FromException(new InvalidOperationException("The mail server is not answering."));
        }

        recorded.Add(message, attachments);

        return Task.CompletedTask;
    }
}
