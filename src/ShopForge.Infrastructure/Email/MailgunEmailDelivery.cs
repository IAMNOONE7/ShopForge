using System.Net.Http.Headers;
using System.Text;
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
    EmailOptions options,
    IStoreContext storeContext,
    ICurrentStoreSettings storeSettings,
    ILogger<MailgunEmailDelivery> logger) : IEmailDelivery
{
    public async Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = await FromAsync(cancellationToken),
            ["to"] = message.To,
            ["subject"] = message.Subject,
            ["text"] = message.Body,
        });

        using var response = await httpClient.PostAsync($"v3/{options.Mailgun.Domain}/messages", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new InvalidOperationException(
                $"Mailgun refused the message with {(int)response.StatusCode}: {Shorten(problem)}");
        }

        logger.LogInformation("E-mail to {Recipient} handed to Mailgun: {Subject}", message.To, message.Subject);
    }

    // Mail about a shop comes from the shop, by name; mail about the company or the platform comes from the
    // platform. The address is the same either way until a store can verify a domain of its own (Stage 20).
    private async Task<string> FromAsync(CancellationToken cancellationToken)
    {
        var name = storeContext.StoreId is null
            ? options.SenderName
            : (await storeSettings.GetAsync(cancellationToken)).Name;

        return $"\"{name.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{options.SenderAddress}>";
    }

    private static string Shorten(string problem) => problem.Length > 200 ? problem[..200] : problem;

    public static void Configure(HttpClient httpClient, EmailOptions options)
    {
        httpClient.BaseAddress = new Uri(options.Mailgun.BaseUrl.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{options.Mailgun.ApiKey}")));
    }
}
