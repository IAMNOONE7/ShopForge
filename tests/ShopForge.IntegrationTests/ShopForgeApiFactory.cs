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

    public RecordedEmails Emails { get; } = new();

    internal RecordingEmailDelivery EmailDelivery { get; }

    // Tests do not wait for the worker's ten-second tick; they run the outbox when they need what it delivers.
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

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _fileStorage.StartAsync());
        await Services.MigrateDatabaseAsync();
        await Services.CreateFileStorageContainerAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ShopForge", _database.GetConnectionString());
        builder.UseSetting("ConnectionStrings:FileStorage", _fileStorage.GetConnectionString());

        // The suite signs in far more often than a person would; the limiter is exercised by its own test instead.
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "10000");

        // The test host speaks plain HTTP, so cookies cannot be marked Secure the way a deployment marks them.
        builder.UseSetting("Security:RequireSecureCookies", "false");
        // Only the last hop is faked: registration still writes an outbox message, which the dispatcher delivers.
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailDelivery>(EmailDelivery));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        await _fileStorage.DisposeAsync();
    }
}

// Messages normally go to the log (D-053); tests read them here to follow the links a customer would click.
public sealed class RecordedEmails
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public void Add(EmailMessage message) => _messages.Enqueue(message);

    public IReadOnlyList<EmailMessage> For(string recipient) => [.. _messages.Where(message => message.To == recipient)];

    public string? LatestLinkFor(string recipient)
    {
        var body = For(recipient).LastOrDefault()?.Body;
        var token = body?.Split("token=").ElementAtOrDefault(1)?.Split(' ')[0];

        return token?.TrimEnd('.');
    }
}

// Tests run side by side against one host, so a test that needs delivery to fail says for whom.
internal sealed class RecordingEmailDelivery(RecordedEmails recorded) : IEmailDelivery
{
    private readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.OrdinalIgnoreCase);

    public void FailFor(string recipient) => _failing[recipient] = 0;

    public void StopFailingFor(string recipient) => _failing.TryRemove(recipient, out _);

    public Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (_failing.ContainsKey(message.To))
        {
            return Task.FromException(new InvalidOperationException("The mail server is not answering."));
        }

        recorded.Add(message);

        return Task.CompletedTask;
    }
}
