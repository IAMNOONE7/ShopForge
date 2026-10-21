using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Email;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Email;

// The last hop. Everything before it — the outbox, the retries, the dead letters (D-067, D-068) — is already in
// place, so this only has to send one message and be honest when it cannot: throwing is what makes the outbox
// back off and eventually put the message where somebody can see it (D-120).
internal sealed class MailgunEmailDelivery(
    HttpClient httpClient,
    IMailSenders senders,
    EmailOptions options,
    IStoreContext storeContext,
    ICurrentStoreSettings storeSettings,
    ILogger<MailgunEmailDelivery> logger) : IEmailDelivery
{
    public async Task DeliverAsync(EmailMessage message, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken)
    {
        // The store's own domain and key once it has them, the platform's until then (D-211).
        var sender = await senders.CurrentAsync(cancellationToken);

        // Multipart either way: a form that sometimes changes shape is a form with two ways to be wrong.
        using var content = new MultipartFormDataContent
        {
            { new StringContent(await FromAsync(sender, cancellationToken)), "from" },
            { new StringContent(message.To), "to" },
            { new StringContent(message.Subject), "subject" },
            { new StringContent(message.Body), "text" },
        };

        if (sender.ReplyTo is { Length: > 0 } replyTo)
        {
            content.Add(new StringContent(replyTo), "h:Reply-To");
        }

        // The store rides along so a bounce arriving hours later can be filed against the right shop (D-123);
        // an address alone does not say whose customer it is.
        if (storeContext.StoreId is { } storeId)
        {
            content.Add(new StringContent(storeId.ToString()), $"v:{MailgunVariables.Store}");
        }

        // Both parts travel: a client that will not show the HTML falls back to the words.
        if (message.HtmlBody is { Length: > 0 } html)
        {
            content.Add(new StringContent(html), "html");
        }

        foreach (var attachment in attachments)
        {
            var file = new ByteArrayContent(attachment.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(attachment.ContentType);
            content.Add(file, "attachment", attachment.FileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v3/{sender.Domain}/messages") { Content = content };
        request.Headers.Authorization = MailgunApi.Basic(sender.Key);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new InvalidOperationException(
                $"Mailgun refused the message with {(int)response.StatusCode}: {Shorten(problem)}");
        }

        logger.LogInformation("E-mail to {Recipient} handed to Mailgun: {Subject}", message.To, message.Subject);
    }

    // Mail about a shop comes from the shop, by name; mail about the company or the platform comes from the
    // platform. The address is the store's own once it sends from a domain of its own, the platform's until then.
    private async Task<string> FromAsync(MailSender sender, CancellationToken cancellationToken)
    {
        var name = storeContext.StoreId is null
            ? options.SenderName
            : (await storeSettings.GetAsync(cancellationToken)).Name;

        return $"\"{name.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{sender.Address}>";
    }

    private static string Shorten(string problem) => problem.Length > 200 ? problem[..200] : problem;

    // Each message carries the key of the domain it goes out through.
    public static void Configure(HttpClient httpClient, EmailOptions options) =>
        httpClient.BaseAddress = new Uri(options.Mailgun.BaseUrl.TrimEnd('/') + "/");
}
