using System.Reflection;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Caching;
using ShopForge.Infrastructure.Connections;
using ShopForge.Infrastructure.Diagnostics;
using ShopForge.Infrastructure.Dns;
using ShopForge.Infrastructure.Documents;
using ShopForge.Infrastructure.Edge;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Idempotency;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Infrastructure.Shipping.Packeta;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Caching;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Email;
using ShopForge.Shared.Files;
using ShopForge.Shared.Http;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;

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
        services.AddSingleton<SchemaGate>();

        // Without this every replica keeps its own key ring in its own filesystem, so a cookie one replica
        // issued is unreadable by the next and a sign-in does not survive the load balancer — and a container
        // restart, which every deployment is, signs everybody out. It never shows on one machine, where the
        // ring sits in the home directory and outlives every restart. The database is what every replica
        // already shares (D-191). The name keeps the keys valid however the containers lay out their paths.
        services.AddDataProtection()
            .SetApplicationName("ShopForge")
            .PersistKeysToDbContext<ShopForgeDbContext>();

        // What a replica keeps of what every replica reads, and the channel that tells it to forget (D-202).
        services.AddMemoryCache();
        services.AddSingleton(provider => new CacheChannel(connectionString, provider.GetRequiredService<ILogger<CacheChannel>>()));
        services.AddSingleton<ReplicaCache>();
        services.AddSingleton<IReplicaCache>(provider => provider.GetRequiredService<ReplicaCache>());
        services.AddHostedService<CacheListener>();

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
        services.AddSingleton(new OutboxOptions(TimeSpan.FromSeconds(configuration.GetValue("Outbox:IntervalSeconds", 10))));
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
        services.AddSingleton<IScheduledPass>(provider => provider.GetRequiredService<StoreMaintenance>());
        services.AddSingleton<StoreCatchUp>();
        services.AddSingleton<IScheduledPass>(provider => provider.GetRequiredService<StoreCatchUp>());

        // Every pass any module schedules, each taken by one replica when it falls due (D-202).
        services.AddSingleton<RunSchedule>();
        services.AddSingleton(new ScheduleOptions(configuration.GetValue("Schedules:Enabled", true)));
        services.AddHostedService<ScheduledPasses>();

        AddProvisioning(services, configuration);
        AddEdgeDomains(services, configuration, connectionString);
        AddDnsZones(services, configuration);

        AddSecretStore(services, configuration);
        AddComgate(services, configuration);
        AddPacketa(services, configuration);
        AddEmailDelivery(services, configuration);

        var stripe = configuration.GetSection(StripeOptions.Section).Get<StripeOptions>() ?? new StripeOptions();

        // Without keys the provider is simply not offered, which is how development and CI run (D-059). With them it
        // is registered, so payments already started through it are still heard about and refunded, and a store may
        // choose it only where the deployment says every store is its owner's (D-196).
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
        services.AddScoped<ComgateTransactions>();
        services.AddScoped<IPaymentNotifications, ComgateNotifications>();
        services.AddScoped<IPaymentEnquiries, ComgateEnquiries>();
        services.AddScoped<IPaymentRefunds, ComgateRefunds>();
        services.AddScoped<ISettlementStatements, ComgateStatements>();
    }

    // The same arrangement as Comgate's, for the same reason: the account belongs to the store, so the carrier
    // is always registered and a store that has connected none is simply not offered its methods (D-138, D-155).
    private static void AddPacketa(IServiceCollection services, IConfiguration configuration)
    {
        // The point check is the widget's own endpoint, under the widget's address (D-213). Booking, which is V8's, is
        // Packeta's REST API on another host and will have an address of its own.
        var widgetAddress = configuration["Shipping:Packeta:WidgetAddress"] ?? "https://widget.packeta.com/v6/";

        services.AddHttpClient<IPacketaClient, PacketaHttpClient>(client => client.BaseAddress = new Uri(widgetAddress));
        services.AddScoped<IShippingProvider, PacketaShippingProvider>();
    }

    // Operations that outlive a request, carried through by a worker on every replica, each step by one of them (D-210).
    // Off where something else drives them: the integration tests, which run the steps they need themselves.
    private static void AddProvisioning(IServiceCollection services, IConfiguration configuration)
    {
        var defaults = ProvisioningOptions.Default;
        var options = defaults with
        {
            Enabled = configuration.GetValue("Provisioning:Enabled", defaults.Enabled),
            Interval = TimeSpan.FromSeconds(configuration.GetValue("Provisioning:IntervalSeconds", defaults.Interval.TotalSeconds)),
            Lease = TimeSpan.FromSeconds(configuration.GetValue("Provisioning:LeaseSeconds", defaults.Lease.TotalSeconds)),
            StepTimeout = TimeSpan.FromSeconds(configuration.GetValue("Provisioning:StepTimeoutSeconds", defaults.StepTimeout.TotalSeconds)),
        };

        // A lease that ran out while its step was still running would let a second replica start the same step.
        if (options.Lease <= options.StepTimeout)
        {
            throw new InvalidOperationException("Provisioning:LeaseSeconds must be longer than Provisioning:StepTimeoutSeconds.");
        }

        services.AddSingleton(options);
        services.AddScoped<IProvisioning, Operations>();
        services.AddScoped<OperationAdmin>();
        services.AddSingleton<OperationRunner>();
        services.AddHostedService<OperationWorker>();
    }

    // Merchants' domains at the edge, written as the app's own identity (D-212). A deployment without the profile's
    // names binds nothing there, which is how development and most tests run.
    private static void AddEdgeDomains(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var edge = configuration.GetSection(EdgeDomainOptions.Section).Get<EdgeDomainOptions>() ?? new EdgeDomainOptions();

        if (edge.IsPartlyConfigured)
        {
            throw new InvalidOperationException(
                "Edge:Domains names some of the profile and not all: SubscriptionId, ResourceGroup, Profile, Endpoint, EndpointHost and WafPolicyId go together.");
        }

        if (!edge.IsConfigured)
        {
            services.AddSingleton<IEdgeDomains, NoEdgeDomains>();

            return;
        }

        services.AddSingleton(edge);
        services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
        services.AddSingleton(new EdgeRouteLock(connectionString));
        services.AddTransient<ArmTokenHandler>();
        services.AddHttpClient<ArmApi>(client => client.BaseAddress = new Uri(edge.ManagementUrl.TrimEnd('/') + "/"))
            .AddHttpMessageHandler<ArmTokenHandler>();
        services.AddScoped<IEdgeDomains, FrontDoorDomains>();
    }

    // The zones the platform runs, at Cloudflare (D-212). Without a token and an account, every record is the
    // merchant's to publish.
    private static void AddDnsZones(IServiceCollection services, IConfiguration configuration)
    {
        var cloudflare = configuration.GetSection(CloudflareOptions.Section).Get<CloudflareOptions>() ?? new CloudflareOptions();

        if (cloudflare.IsPartlyConfigured)
        {
            throw new InvalidOperationException("Dns:Cloudflare needs both its ApiToken and its AccountId, or neither.");
        }

        if (!cloudflare.IsConfigured)
        {
            services.AddSingleton<IDnsZones, NoDnsZones>();

            return;
        }

        services.AddSingleton(cloudflare);
        services.AddHttpClient<IDnsZones, CloudflareZones>(client => CloudflareZones.Configure(client, cloudflare));
    }

    private static void AddSecretStore(IServiceCollection services, IConfiguration configuration)
    {
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

        // Whether mail leaves the building is a fact about this deployment, so the answer comes from here rather
        // than from a module that cannot see the configuration (D-190).
        services.AddScoped<IStorePublishCheck, EmailPublishCheck>();

        // A store's own sending domain, connected and removed as operations (D-211). Registered whatever carries the
        // mail, because the engine asks for every step it knows; connecting one refuses unless Mailgun does.
        services.AddScoped<IMailSenders, StoreSenders>();
        services.AddScoped<SendingDomainAdmin>();
        services.AddHttpClient<MailgunApi>(client => MailgunApi.Configure(client, email));
        services.AddSingleton<IOperationKind, ConnectSendingDomain>();
        services.AddSingleton<IOperationKind, RemoveSendingDomain>();
        services.AddScoped<IOperationStep, RegisterSendingDomain>();
        services.AddScoped<IOperationStep, MailgunSendingDomain>();
        services.AddScoped<IOperationStep, MailgunSendingKey>();
        services.AddScoped<IOperationStep, MailgunVerification>();
        services.AddScoped<IOperationStep, ActivateSendingDomain>();
        services.AddScoped<IOperationStep, DeactivateSendingDomain>();
        services.AddScoped<IOperationStep, RevokeSendingKey>();
        services.AddScoped<IOperationStep, DeleteMailgunDomain>();
        services.AddScoped<IOperationStep, ForgetSendingDomain>();

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
