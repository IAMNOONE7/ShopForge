using System.Reflection;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Diagnostics;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Email;
using ShopForge.Shared.Files;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> entityConfigurationAssemblies)
    {
        var connectionString = configuration.GetConnectionString("ShopForge")
            ?? throw new InvalidOperationException("Connection string 'ShopForge' is not configured.");

        services.AddObservability(configuration);
        services.AddSingleton(new EntityConfigurationAssemblies([.. entityConfigurationAssemblies]));
        services.AddDbContext<ShopForgeDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ShopForgeDbContext>());

        var fileStorageConnectionString = configuration.GetConnectionString("FileStorage")
            ?? throw new InvalidOperationException("Connection string 'FileStorage' is not configured.");
        var containerName = configuration["FileStorage:Container"] ?? "media";

        services.AddSingleton(new BlobContainerClient(fileStorageConnectionString, containerName));
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        services.AddSingleton<IEmailDelivery, LoggingEmailDelivery>();
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IEventHandler<EmailRequested>, EmailRequestedHandler>();
        services.AddSingleton<OutboxEventTypes>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddScoped<OutboxAdmin>();
        services.AddSingleton<StoreMaintenance>();
        services.AddHostedService<MaintenanceWorker>();

        var stripe = configuration.GetSection(StripeOptions.Section).Get<StripeOptions>() ?? new StripeOptions();

        // Without keys the provider is simply not offered, which is how development and CI run (D-059).
        if (stripe.IsConfigured)
        {
            services.AddSingleton(stripe);
            services.AddSingleton<ICheckoutSessions, StripeCheckoutSessions>();
            services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
            services.AddSingleton<IPaymentNotifications, StripeNotifications>();
        }

        return services;
    }
}
