using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.UnitTests.Email;

public sealed class EmailProviderChoiceTests
{
    [Fact]
    public void Nothing_configured_writes_to_the_log_so_development_and_tests_need_no_account()
    {
        using var services = Build(new Dictionary<string, string?>());

        Assert.IsType<LoggingEmailDelivery>(DeliveryOf(services));
    }

    [Fact]
    public void Choosing_mailgun_and_configuring_it_sends_through_mailgun()
    {
        using var services = Build(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "mailgun",
            ["Email:SenderAddress"] = "no-reply@mg.shopforge.test",
            ["Email:Mailgun:ApiKey"] = "key-123",
            ["Email:Mailgun:Domain"] = "mg.shopforge.test",
        });

        Assert.IsType<MailgunEmailDelivery>(DeliveryOf(services));
    }

    // Asking for a provider and not giving it what it needs would otherwise be discovered from mail nobody got.
    [Fact]
    public void Choosing_mailgun_without_its_settings_stops_the_application_starting()
    {
        var problem = Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "mailgun",
        }));

        Assert.Contains("Email:Mailgun:ApiKey", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_nobody_has_written_is_refused_by_name()
    {
        var problem = Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "pigeon",
        }));

        Assert.Contains("pigeon", problem.Message, StringComparison.Ordinal);
    }

    // Resolved for real rather than read off the registration, so the test also says the thing can be built.
    private static IEmailDelivery DeliveryOf(ServiceProvider services)
    {
        using var scope = services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IEmailDelivery>();
    }

    private static ServiceProvider Build(IDictionary<string, string?> settings)
    {
        settings["ConnectionStrings:ShopForge"] = "Host=localhost;Database=none";
        settings["ConnectionStrings:FileStorage"] = "UseDevelopmentStorage=true";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScoped<StoreContext>();
        services.AddScoped<IStoreContext>(provider => provider.GetRequiredService<StoreContext>());
        services.AddScoped<ICurrentStoreSettings, NoStoreSettings>();

        return services.AddInfrastructure(configuration, []).BuildServiceProvider();
    }

    private sealed class NoStoreSettings : ICurrentStoreSettings
    {
        public Task<StoreSettings> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No store is in scope.");
    }
}
