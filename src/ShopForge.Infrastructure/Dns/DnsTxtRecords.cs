using DnsClient;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Dns;

namespace ShopForge.Infrastructure.Dns;

internal sealed class DnsTxtRecords(ILogger<DnsTxtRecords> logger) : IDnsTxtRecords
{
    private static readonly LookupClient Client = new(new LookupClientOptions
    {
        // A merchant is watching this happen, so it gives up quickly rather than holding the request open.
        Timeout = TimeSpan.FromSeconds(5),
        UseCache = false,
        Retries = 1,
    });

    public async Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await Client.QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken);

            return [.. answer.Answers.TxtRecords().SelectMany(record => record.Text)];
        }
        catch (DnsResponseException exception)
        {
            // A name that does not resolve is the ordinary case while a merchant is still editing their DNS.
            logger.LogInformation(exception, "No TXT record could be read for {Name}.", name);

            return [];
        }
    }
}
