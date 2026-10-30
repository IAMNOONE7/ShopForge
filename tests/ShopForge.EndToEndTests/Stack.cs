using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopForge.EndToEndTests;

// The stack these tests run against, as `tests/e2e/stack.sh up` describes it in .e2e/stack.json — or wherever
// SHOPFORGE_E2E_STACK says. Without one every test is skipped, which is what a build that started no stack sees: these
// prove the harness against a real API and rig, and there is nothing to prove them against there (D-203).
//
// The journeys are one class, so they run one at a time: moving the clock moves it for every run on the stack.
internal sealed class Stack
{
    private Stack(Uri api, Uri rig)
    {
        Api = api;
        Rig = rig;
    }

    public Uri Api { get; }

    public Uri Rig { get; }

    public static Stack Required()
    {
        var path = Environment.GetEnvironmentVariable("SHOPFORGE_E2E_STACK") ?? Path.Combine(RepositoryRoot(), ".e2e", "stack.json");

        if (!File.Exists(path))
        {
            Assert.Skip($"No end-to-end stack is running ({path} does not exist); start one with tests/e2e/stack.sh up.");
        }

        using var described = JsonDocument.Parse(File.ReadAllText(path));

        return new Stack(
            new Uri(described.RootElement.GetProperty("api").GetString()!),
            new Uri(described.RootElement.GetProperty("rig").GetString()!));
    }

    public Task<JsonElement> StartRunAsync(CancellationToken cancellationToken) => StartRunAsync(toolShop: false, cancellationToken);

    public async Task<JsonElement> StartRunAsync(bool toolShop, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = Rig };
        using var response = await client.PostAsync(toolShop ? "runs?toolShop=true" : "runs", null, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"The rig did not start a run: {body}");

        return body;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> RigAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = Rig };
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShopForge.slnx")))
            {
                return directory.FullName;
            }
        }

        return AppContext.BaseDirectory;
    }
}
