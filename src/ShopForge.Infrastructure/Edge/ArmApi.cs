using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Provisioning;

namespace ShopForge.Infrastructure.Edge;

// Azure Resource Manager's REST API, for the profile's resources only, as the app's own identity (D-212). A refusal is
// a ProviderException carrying the Retry-After it came with. Long-running writes are not polled through their
// operation URL, which needs a role on the whole resource group; the step that waits reads the resource itself.
internal sealed class ArmApi(HttpClient httpClient, TimeProvider clock)
{
    public const string ApiVersion = "2025-04-15";

    private const string Provider = "Azure";

    public Task<JsonObject?> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

    public Task<JsonObject?> PutAsync(string path, JsonObject body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, path, body, cancellationToken);

    public Task<JsonObject?> PostAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body: null, cancellationToken);

    // Gone already is gone.
    public Task<JsonObject?> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, path, body: null, cancellationToken);

    // A GET or a DELETE of something that is not there answers null; anything else that is not a success throws.
    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, WithVersion(path));

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && (method == HttpMethod.Get || method == HttpMethod.Delete))
        {
            return null;
        }

        // A role assignment takes minutes to reach Resource Manager, so a refusal of who the app is, or of what it may do,
        // is tried again like a failure that goes away; one that does not still stops for a person after the last try.
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;

            throw new ProviderException(
                Provider, status, MessageOf(text), ProviderHeaders.RetryAfter(response, clock.GetUtcNow()), permanent: status is 401 or 403 ? false : null);
        }

        return text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
    }

    // A next page's link arrives whole, version and all.
    private static string WithVersion(string path) =>
        path.Contains("api-version=", StringComparison.Ordinal) ? path : $"{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}api-version={ApiVersion}";

    private static string MessageOf(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? Shorten(body);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Shorten(body);
        }
    }

    private static string Shorten(string text) => text.Length > 200 ? text[..200] : text;
}

// Signs each request with a token for the management endpoint. The credential keeps its tokens until they lapse.
internal sealed class ArmTokenHandler(TokenCredential credential, EdgeDomainOptions options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([options.TokenScope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        return await base.SendAsync(request, cancellationToken);
    }
}
