using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopForge.TestRig.Runs;

// One person's conversation with the API: their own cookies, and every answer checked against the one expected, so a
// run that cannot be set up says which step refused and why instead of carrying on half-made.
internal sealed class ApiSession : IDisposable
{
    private readonly HttpClient _client;

    public ApiSession(Uri apiOrigin)
    {
        _client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = false })
        {
            BaseAddress = apiOrigin,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, path), HttpStatusCode.OK, cancellationToken);

    public Task<JsonElement> PostAsync(string path, object? body, HttpStatusCode expected, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) }, expected, cancellationToken);

    public Task<JsonElement> PutAsync(string path, object body, HttpStatusCode expected, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) }, expected, cancellationToken);

    public Task<JsonElement> PutFileAsync(string path, byte[] file, string fileName, string contentType, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        return SendAsync(new HttpRequestMessage(HttpMethod.Put, path) { Content = new MultipartFormDataContent { { content, "file", fileName } } }, expected, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using (request)
        {
            HttpResponseMessage answered;

            try
            {
                answered = await _client.SendAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new RunSetupException($"{request.Method} {request.RequestUri} could not reach the API: {exception.Message}");
            }

            using var response = answered;
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode != expected)
            {
                throw new RunSetupException($"{request.Method} {request.RequestUri} answered {(int)response.StatusCode} where {(int)expected} was expected: {Shorten(text)}");
            }

            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    private static string Shorten(string text) => text.Length > 600 ? text[..600] : text;
}

internal sealed class RunSetupException(string message) : Exception(message);
