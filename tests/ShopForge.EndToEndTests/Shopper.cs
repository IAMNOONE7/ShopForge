using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopForge.EndToEndTests;

// A shopper on one store's host, with the cookies a browser would keep, talking to the API as the storefront's proxy
// does: the store's host name in the Host header, so the API resolves the store exactly as it does for a browser.
internal sealed class Shopper : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _host;

    public Shopper(Uri api, string host)
    {
        _host = host;
        _client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = false })
        {
            BaseAddress = api,
        };
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Host = _host;

        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    // What the shopper does on the gateway's page: press one of its buttons. The answer is where the gateway sends the
    // browser back to, which is ShopForge's own page.
    public async Task<Uri> ChooseOnGatewayAsync(string payPage, string outcome, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(
            payPage, new FormUrlEncodedContent([new KeyValuePair<string, string>("outcome", outcome)]), cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return response.Headers.Location!;
    }

    public void Dispose() => _client.Dispose();
}
