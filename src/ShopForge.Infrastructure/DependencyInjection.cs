using System.Reflection;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Diagnostics;
using ShopForge.Infrastructure.Dns;
using ShopForge.Infrastructure.Documents;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Email;
using ShopForge.Shared.Files;
using ShopForge.Shared.Maintenance;
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
        services.AddSingleton<IDocumentRenderer, MigraDocRenderer>();
        services.AddSingleton<IDnsTxtRecords, DnsTxtRecords>();
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IEventHandler<EmailRequested>, EmailRequestedHandler>();
        services.AddSingleton<OutboxEventTypes>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddScoped<OutboxAdmin>();
        services.AddScoped<EmailSuppression>();
        services.AddScoped<IEmailSuppression>(provider => provider.GetRequiredService<EmailSuppression>());
        services.AddScoped<MailgunWebhook>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<AuditReader>();
        services.AddScoped<IMaintenanceOutsideStores, OutboxCleanup>();
        services.AddScoped<IMaintenanceOutsideStores, AuditCleanup>();
        services.AddSingleton<StoreMaintenance>();
        services.AddHostedService<MaintenanceWorker>();

        AddEmailDelivery(services, configuration);

        var stripe = configuration.GetSection(StripeOptions.Section).Get<StripeOptions>() ?? new StripeOptions();

        // Without keys the provider is simply not offered, which is how development and CI run (D-059).
        if (stripe.IsConfigured)
        {
            services.AddSingleton(stripe);
            services.AddSingleton<ICheckoutSessions, StripeCheckoutSessions>();
            services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
            services.AddSingleton<IPaymentNotifications, StripeNotifications>();
            services.AddSingleton<IRefunds, StripeRefundApi>();
            services.AddSingleton<IPaymentRefunds, StripeRefunds>();
        }

        return services;
    }

    // One seam, one setting: another provider is a class implementing IEmailDelivery and a name here (D-120).
    // Nothing configured means the log, which is how development and the tests run.
    private static void AddEmailDelivery(IServiceCollection services, IConfiguration configuration)
    {
        var email = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();

        // Registered whichever provider carries the mail: the sender's name is what a message with no store
        // behind it goes out under (D-111).
        services.AddSingleton(email);

        if (string.Equals(email.Provider, EmailOptions.LogProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailDelivery, LoggingEmailDelivery>();

            return;
        }

        if (!string.Equals(email.Provider, EmailOptions.MailgunProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{email.Provider}' is not an e-mail provider ShopForge knows.");
        }

        // Asking for a provider and not giving it what it needs is a mistake worth making at startup, not one to
        // discover from mail nobody received.
        if (!email.Mailgun.IsConfigured || email.SenderAddress.Length == 0)
        {
            throw new InvalidOperationException(
                "Mailgun needs Email:Mailgun:ApiKey, Email:Mailgun:Domain and Email:SenderAddress.");
        }

        services.AddHttpClient<IEmailDelivery, MailgunEmailDelivery>(client => MailgunEmailDelivery.Configure(client, email));
    }
}
