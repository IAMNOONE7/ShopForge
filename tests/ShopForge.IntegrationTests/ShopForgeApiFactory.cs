using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.IntegrationTests;
using ShopForge.Shared.Email;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ShopForgeApiFactory))]

namespace ShopForge.IntegrationTests;

public sealed class ShopForgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly AzuriteContainer _fileStorage = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public RecordedEmails Emails { get; } = new();

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
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(new RecordingEmailSender(Emails)));
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

internal sealed class RecordingEmailSender(RecordedEmails recorded) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        recorded.Add(message);

        return Task.CompletedTask;
    }
}
