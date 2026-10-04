using System.Reflection;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Connections;
using ShopForge.Infrastructure.Diagnostics;
using ShopForge.Infrastructure.Dns;
using ShopForge.Infrastructure.Documents;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Idempotency;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Infrastructure.Shipping.Packeta;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Email;
using ShopForge.Shared.Files;
using ShopForge.Shared.Http;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;

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
        services.AddScoped<IIdempotentRequests, IdempotentRequests>();
        services.AddScoped<IProviderConnections, ProviderConnections>();
        services.AddScoped<IMerchantConnections, MerchantConnections>();
        services.AddScoped<ProviderConnectionAdmin>();
        services.AddScoped<AuditReader>();
        services.AddScoped<IMaintenanceOutsideStores, OutboxCleanup>();
        services.AddScoped<IMaintenanceOutsideStores, AuditCleanup>();
        services.AddScoped<IMaintenanceOutsideStores, IdempotencyCleanup>();
        services.AddSingleton<StoreMaintenance>();
        services.AddHostedService<MaintenanceWorker>();

        AddSecretStore(services, configuration);
        AddComgate(services, configuration);
        AddPacketa(services, configuration);
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
    // Where a merchant's own provider credentials are kept. A vault when there is one, configuration otherwise,
    // which is development, CI and a single merchant running their own installation (D-138, D-139).
    // Comgate needs nothing from the deployment: its merchant and secret belong to the store, so the provider
    // is always registered and simply not offered where nobody has connected one (D-138).
    private static void AddComgate(IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["Payments:Comgate:BaseAddress"] ?? "https://payments.comgate.cz/";

        services.AddHttpClient<IComgatePayments, ComgateHttpPayments>(client => client.BaseAddress = new Uri(baseAddress));
        services.AddScoped<IPaymentProvider, ComgatePaymentProvider>();
        services.AddScoped<IPaymentNotifications, ComgateNotifications>();
    }

    // The same arrangement as Comgate's, for the same reason: the account belongs to the store, so the carrier
    // is always registered and a store that has connected none is simply not offered its methods (D-138, D-155).
    private static void AddPacketa(IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["Shipping:Packeta:BaseAddress"] ?? "https://www.zasilkovna.cz/api/";

        // Packeta takes the API password in the path of the URL, and the default HTTP logging writes every
        // request URI at Information. That put the password in the log the first time this ran (D-139 forbids
        // exactly that), so this client does no request logging at all. Anything added back here must redact
        // the path, not the headers.
        services.AddHttpClient<IPacketaClient, PacketaHttpClient>(client => client.BaseAddress = new Uri(baseAddress))
            .RemoveAllLoggers();
        services.AddScoped<IShippingProvider, PacketaShippingProvider>();
    }

    private static void AddSecretStore(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        if (configuration[SecretStoreOptions.VaultUri] is not { Length: > 0 } vaultUri)
        {
            services.AddSingleton<ISecretStore, ConfigurationSecretStore>();

            return;
        }

        services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
        services.AddSingleton<ISecretStore, KeyVaultSecretStore>();
    }

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
