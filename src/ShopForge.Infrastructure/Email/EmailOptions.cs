namespace ShopForge.Infrastructure.Email;

// Which provider carries the mail, and who it comes from. A provider is a class behind `IEmailDelivery` and a
// name in here; swapping one for another is configuration, not a rewrite (D-120).
internal sealed class EmailOptions
{
    public const string Section = "Email";

    public const string LogProvider = "log";

    public const string MailgunProvider = "mailgun";

    public string Provider { get; init; } = LogProvider;

    // The address mail is sent from. It has to belong to a domain the provider has verified, so it is the
    // platform's rather than the store's until Stage 20 gives stores their own domains.
    public string SenderAddress { get; init; } = string.Empty;

    // Used as the display name when no store is in scope: an invitation, an operator's reset (D-111).
    public string SenderName { get; init; } = "ShopForge";

    public MailgunOptions Mailgun { get; init; } = new();
}

internal sealed class MailgunOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Domain { get; init; } = string.Empty;

    // Mailgun keeps European and American accounts on different hosts, and sending to the wrong one fails in a
    // way that reads like a bad key.
    public string BaseUrl { get; init; } = "https://api.eu.mailgun.net";

    public bool IsConfigured => ApiKey.Length > 0 && Domain.Length > 0;
}
