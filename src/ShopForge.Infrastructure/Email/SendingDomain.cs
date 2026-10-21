using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Shared.Email;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Email;

// A store's own domain to send its mail from, and how far it has got (D-211). It is always a subdomain of a domain
// the store proved it owns, so its SPF record and DKIM key sit on a name of their own and never meet the mailbox
// provider's on the domain itself. Mail comes from an address on either, which relaxed DMARC alignment accepts.
// Until it is active, the store's mail goes out the way it always has: from the platform's domain.
internal sealed class SendingDomain : IStoreOwned
{
    public const int MaxDomainLength = 253;

    private SendingDomain()
    {
    }

    public SendingDomain(
        Guid tenantId, Guid storeId, string domain, string alignedDomain, string senderAddress, string? replyTo, string region, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        Domain = domain;
        AlignedDomain = alignedDomain;
        SenderAddress = senderAddress;
        ReplyTo = replyTo;
        Region = region;
        State = SendingDomainState.Connecting;
        CreatedAt = now;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    // Where the mail is sent from, e.g. mail.shop.cz, and the proved domain it sits under, e.g. shop.cz.
    public string Domain { get; private set; } = null!;

    public string AlignedDomain { get; private set; } = null!;

    public string SenderAddress { get; private set; } = null!;

    public string? ReplyTo { get; private set; }

    // The provider's region the domain was made in, which is where its mail has to be sent.
    public string Region { get; private set; } = null!;

    public SendingDomainState State { get; private set; }

    // The records the provider asked for, as it returned them, and whether it last found each one.
    public string? Records { get; private set; }

    // The domain's own sending key, and where its secret is kept. Never the secret.
    public string? KeyId { get; private set; }

    public string? KeySecretName { get; private set; }

    // The operation connecting or removing it.
    public Guid? OperationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public void Begin(Guid operationId, DateTimeOffset now)
    {
        OperationId = operationId;
        ChangedAt = now;
    }

    public void Record(string records, DateTimeOffset now)
    {
        Records = records;

        if (State == SendingDomainState.Connecting)
        {
            State = SendingDomainState.AwaitingDns;
        }

        ChangedAt = now;
    }

    public void UseKey(string keyId, string secretName, DateTimeOffset now)
    {
        KeyId = keyId;
        KeySecretName = secretName;
        ChangedAt = now;
    }

    public void ForgetKey(DateTimeOffset now)
    {
        KeyId = null;
        KeySecretName = null;
        ChangedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        State = SendingDomainState.Active;
        VerifiedAt ??= now;
        ChangedAt = now;
    }

    // The store's mail goes back to the platform's domain the moment this is written, before anything is taken down.
    public void Deactivate(Guid operationId, DateTimeOffset now)
    {
        State = SendingDomainState.Removing;
        OperationId = operationId;
        ChangedAt = now;
    }
}

public enum SendingDomainState
{
    Connecting,
    AwaitingDns,
    Active,
    Removing,
}

internal sealed class SendingDomainEntityConfiguration : IEntityTypeConfiguration<SendingDomain>
{
    public void Configure(EntityTypeBuilder<SendingDomain> builder)
    {
        builder.ToTable("sending_domains", "messaging");

        builder.Property(domain => domain.Domain).HasMaxLength(SendingDomain.MaxDomainLength);
        builder.Property(domain => domain.AlignedDomain).HasMaxLength(SendingDomain.MaxDomainLength);
        builder.Property(domain => domain.SenderAddress).HasMaxLength(Emails.MaxLength);
        builder.Property(domain => domain.ReplyTo).HasMaxLength(Emails.MaxLength);
        builder.Property(domain => domain.Region).HasMaxLength(10);
        builder.Property(domain => domain.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(domain => domain.KeyId).HasMaxLength(200);
        builder.Property(domain => domain.KeySecretName).HasMaxLength(120);

        // One per store, and a domain is one store's across the platform: the friendly refusals read first, and these
        // are what catch two requests arriving together.
        builder.HasIndex(domain => domain.StoreId).IsUnique();
        builder.HasIndex(domain => domain.Domain).IsUnique();
    }
}
