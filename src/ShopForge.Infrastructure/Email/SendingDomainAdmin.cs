using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Email;
using ShopForge.Shared.Http;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Email;

// What a store's mail goes out through, and the two things a manager can do about it: connect a domain of the store's
// own, or go back to the platform's (D-211). Both are operations, so the answer is an operation's id, at once; nobody
// waits for DNS.
public sealed class SendingDomainAdmin(
    DbContext dbContext,
    IStoreContext storeContext,
    IStoreDomains storeDomains,
    IProvisioning provisioning,
    ISecretStore secrets,
    EmailOptions options,
    TimeProvider clock)
{
    // What the platform suggests at the proved domain, unless one is there already: DMARC that watches before it
    // blocks, with relaxed alignment, which is what lets mail from a subdomain count as the domain's own.
    public const string DmarcSuggestion = "v=DMARC1; p=none; adkim=r; aspf=r";

    public async Task<StoreMailView> ViewAsync(CancellationToken cancellationToken)
    {
        var sending = await dbContext.Set<SendingDomain>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        return new StoreMailView(
            sending?.State == SendingDomainState.Active,
            options.SenderAddress,
            sending is null ? null : Describe(sending));
    }

    public async Task<SendingDomainChange> ConnectAsync(ConnectSendingDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = HostNames.Normalize(request.Domain);
        var proved = await storeDomains.ProvedAsync(cancellationToken);
        var aligned = domain is null ? null : proved.FirstOrDefault(parent => domain.EndsWith("." + parent, StringComparison.Ordinal));
        var sender = request.SenderAddress?.Trim().ToLowerInvariant();
        var senderDomain = sender is null || !Emails.IsValid(sender) ? null : sender[(sender.IndexOf('@') + 1)..];
        var replyTo = string.IsNullOrWhiteSpace(request.ReplyTo) ? null : request.ReplyTo.Trim();

        var errors = new RequestErrors()
            .Check(domain is not null, "domain", "A valid host name is required, for example mail.yourshop.cz.")
            // Its own name under one the store proved: the records it needs never meet the mailbox provider's on the
            // domain itself, and nobody can take a domain into the platform's mail account that they do not own.
            .Check(
                domain is null || aligned is not null,
                "domain",
                proved.Count == 0
                    ? "Prove a domain of your own first; mail is sent from a name under it, such as mail.yourshop.cz."
                    : $"Send from a name under a domain you have proved, such as mail.{proved[0]}.")
            .Check(senderDomain is not null, "senderAddress", "A valid address is required.")
            .Check(
                senderDomain is null || aligned is null || senderDomain == domain || senderDomain == aligned,
                "senderAddress",
                $"Mail has to come from {aligned} or {domain}, or receiving servers will not believe it is yours.")
            .Check(replyTo is null || Emails.IsValid(replyTo), "replyTo", "A valid address is required.");

        if (errors.Any)
        {
            return SendingDomainChange.Invalid(errors);
        }

        if (!string.Equals(options.Provider, EmailOptions.MailgunProvider, StringComparison.OrdinalIgnoreCase))
        {
            return SendingDomainChange.Conflict("This deployment does not send mail through Mailgun, so a store cannot send from a domain of its own.");
        }

        // The domain's key has to be kept somewhere the application can write: a vault (D-139).
        if (secrets is ConfigurationSecretStore)
        {
            return SendingDomainChange.Conflict("This deployment has no vault to keep a sending key in.");
        }

        if (await dbContext.Set<SendingDomain>().AnyAsync(cancellationToken))
        {
            return SendingDomainChange.Conflict("The store already sends from a domain of its own; remove it first.");
        }

        if (await dbContext.Set<SendingDomain>().IgnoreQueryFilters([TenancyFilters.Store]).AnyAsync(candidate => candidate.Domain == domain, cancellationToken))
        {
            return SendingDomainChange.Conflict("That domain already sends mail for another store.");
        }

        var storeId = storeContext.StoreId!.Value;
        var sending = new SendingDomain(
            storeContext.TenantId!.Value, storeId, domain!, aligned!, sender!, replyTo, MailgunApi.RegionOf(options), clock.GetUtcNow());
        dbContext.Add(sending);

        // The row and the operation are written in one save, and the operation's first step claims the row. A store
        // that is busy with another operation saves neither.
        var started = await provisioning.StartAsync(
            SendingDomainOperations.Connect, domain, new { domain, senderAddress = sender, replyTo }, cancellationToken);

        return started.Started ? SendingDomainChange.Started(started.OperationId) : SendingDomainChange.Busy(started.OperationId);
    }

    public async Task<SendingDomainChange> RemoveAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Set<SendingDomain>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { } sending)
        {
            return SendingDomainChange.NotFound;
        }

        var started = await provisioning.StartAsync(SendingDomainOperations.Remove, sending.Domain, input: null, cancellationToken);

        return started.Started ? SendingDomainChange.Started(started.OperationId) : SendingDomainChange.Busy(started.OperationId);
    }

    private static SendingDomainView Describe(SendingDomain sending) => new(
        sending.Domain,
        sending.State,
        sending.SenderAddress,
        sending.ReplyTo,
        sending.Region,
        sending.OperationId,
        sending.VerifiedAt,
        Records(sending.Records),
        [new DnsRecordView("dmarc", "TXT", $"_dmarc.{sending.AlignedDomain}", DmarcSuggestion, null, null)]);

    // The records as Mailgun asked for them, field by field and nothing added; a receiving record with no name is for
    // the domain itself.
    private static IReadOnlyList<DnsRecordView> Records(string? records)
    {
        if (records is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(records);
        var views = new List<DnsRecordView>();

        foreach (var purpose in new[] { "sending", "receiving" })
        {
            if (!document.RootElement.TryGetProperty(purpose, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            views.AddRange(list.EnumerateArray().Select(record => new DnsRecordView(
                purpose,
                Text(record, "record_type") ?? string.Empty,
                Text(record, "name"),
                Text(record, "value") ?? string.Empty,
                Text(record, "priority"),
                Text(record, "valid"))));
        }

        return views;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed record ConnectSendingDomainRequest(string? Domain, string? SenderAddress, string? ReplyTo);

public sealed record StoreMailView(bool SendsFromOwnDomain, string PlatformAddress, SendingDomainView? Domain);

public sealed record SendingDomainView(
    string Domain,
    SendingDomainState State,
    string SenderAddress,
    string? ReplyTo,
    string Region,
    Guid? OperationId,
    DateTimeOffset? VerifiedAt,
    IReadOnlyList<DnsRecordView> Records,
    IReadOnlyList<DnsRecordView> Recommended);

// Where the record goes, what it is, and whether the provider last found it ("valid", "unknown"). The platform's own
// suggestions are not the provider's, and are listed apart.
public sealed record DnsRecordView(string Purpose, string Type, string? Name, string Value, string? Priority, string? Valid);

public sealed record SendingDomainChange(bool Found, RequestErrors? Errors, string? Refusal, Guid? OperationId, bool AlreadyRunning)
{
    public static SendingDomainChange NotFound { get; } = new(false, null, null, null, false);

    public static SendingDomainChange Invalid(RequestErrors errors) => new(true, errors, null, null, false);

    public static SendingDomainChange Conflict(string refusal) => new(true, null, refusal, null, false);

    public static SendingDomainChange Busy(Guid operationId) =>
        new(true, null, "The store is already running an operation; wait for it to finish, or cancel it.", operationId, true);

    public static SendingDomainChange Started(Guid operationId) => new(true, null, null, operationId, false);
}
