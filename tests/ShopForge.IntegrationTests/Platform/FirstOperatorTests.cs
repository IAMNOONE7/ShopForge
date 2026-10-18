using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShopForge.IntegrationTests.Platform;

// The first person to run a new deployment: named by the environment and invited by the release's own migration job,
// a link by mail and a password of their own choosing, and only while the platform has nobody (D-208).
public sealed partial class FirstOperatorTests(ShopForgeApiFactory factory)
{
    private const string AdminHost = "admin.example.test";
    private const string Password = "a-password-of-their-own";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_environment_s_first_release_invites_the_operator_it_names_who_chooses_a_password_and_signs_in()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);

        var released = await ReleaseAsync(database, "Ops@Example.test");
        var link = await InvitationLinkAsync(database, "ops@example.test");
        await using var serving = Serving(database);
        using var client = serving.CreateClient();
        using var accepted = await client.PostAsJsonAsync("/api/platform/invitations/accept", new { Token = TokenOf(link), Password }, CancellationToken);
        using var signedIn = await client.PostAsJsonAsync("/api/platform/auth/login", new { Email = "ops@example.test", Password }, CancellationToken);
        var audited = await database.TextAsync($"SELECT action AS \"Value\" FROM auditing.audit_entries WHERE action = 'operator.invited'");

        Assert.True(released.ExitCode == 0, released.Output);
        Assert.Contains("Invited the platform's first operator", released.Output, StringComparison.Ordinal);
        Assert.StartsWith($"https://{AdminHost}/platform/invitations/accept?token=", link, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (accepted.StatusCode, signedIn.StatusCode));
        Assert.Single(audited);
    }

    // A letter that never arrived is one release, or one start of the job, away from a new one; only the newest works.
    [Fact]
    public async Task Every_release_while_nobody_runs_the_platform_sends_a_fresh_invitation_in_place_of_the_last()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await ReleaseAsync(database, "ops@example.test");
        var first = await InvitationLinkAsync(database, "ops@example.test");

        var again = await ReleaseAsync(database, "ops@example.test");
        await using var serving = Serving(database);
        using var client = serving.CreateClient();
        using var acceptedFirst = await client.PostAsJsonAsync("/api/platform/invitations/accept", new { Token = TokenOf(first), Password }, CancellationToken);
        using var acceptedNewest = await client.PostAsJsonAsync(
            "/api/platform/invitations/accept", new { Token = TokenOf(await InvitationLinkAsync(database, "ops@example.test")), Password }, CancellationToken);

        Assert.True(again.ExitCode == 0, again.Output);
        Assert.Equal(2, (await LettersToAsync(database, "ops@example.test")).Count);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (acceptedFirst.StatusCode, acceptedNewest.StatusCode));
    }

    // A mistyped name is put right in the environment, and the next release withdraws what went to it.
    [Fact]
    public async Task A_name_put_right_withdraws_the_invitation_sent_to_the_wrong_one()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await ReleaseAsync(database, "osp@example.test");
        var mistaken = await InvitationLinkAsync(database, "osp@example.test");

        var corrected = await ReleaseAsync(database, "ops@example.test");
        await using var serving = Serving(database);
        using var client = serving.CreateClient();
        using var acceptedMistake = await client.PostAsJsonAsync("/api/platform/invitations/accept", new { Token = TokenOf(mistaken), Password }, CancellationToken);
        using var accepted = await client.PostAsJsonAsync(
            "/api/platform/invitations/accept", new { Token = TokenOf(await InvitationLinkAsync(database, "ops@example.test")), Password }, CancellationToken);

        Assert.True(corrected.ExitCode == 0, corrected.Output);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (acceptedMistake.StatusCode, accepted.StatusCode));
    }

    // The job is a way in only while there is nobody; after that, operators invite each other through the API, and a
    // name left in the environment does nothing, whatever it has been changed to.
    [Theory]
    [InlineData("intruder@example.test")]
    [InlineData("not-an-address")]
    public async Task Once_there_is_an_operator_a_release_invites_nobody_else(string named)
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await ReleaseAsync(database, "ops@example.test");
        await using var serving = Serving(database);
        using var client = serving.CreateClient();
        using var accepted = await client.PostAsJsonAsync(
            "/api/platform/invitations/accept", new { Token = TokenOf(await InvitationLinkAsync(database, "ops@example.test")), Password }, CancellationToken);

        var next = await ReleaseAsync(database, named);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(next.ExitCode == 0, next.Output);
        Assert.Contains("The platform has an operator, so nobody was invited.", next.Output, StringComparison.Ordinal);
        Assert.Empty(await LettersToAsync(database, named));
    }

    // A release that cannot address its invitation stops before anything serves, rather than inviting nobody quietly.
    [Theory]
    [InlineData("not-an-address", true, "is not an e-mail address")]
    [InlineData("ops@example.test", false, "no admin host")]
    public async Task A_name_the_release_cannot_address_stops_the_release(string email, bool withAdminHost, string reason)
    {
        await using var database = await OwnDatabase.CreateAsync(factory);

        var refused = await ReleaseAsync(database, email, withAdminHost);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains(reason, refused.Output, StringComparison.Ordinal);
        Assert.Empty(await database.TextAsync($"SELECT email AS \"Value\" FROM platform.platform_invitations"));
    }

    [Fact]
    public async Task An_environment_that_names_nobody_invites_nobody()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);

        var migrated = await ApiCommand.RunAsync(factory, database, ["migrate"], new Dictionary<string, string> { ["Frontends__AdminHost"] = AdminHost });

        Assert.True(migrated.ExitCode == 0, migrated.Output);
        Assert.Empty(await database.TextAsync($"SELECT email AS \"Value\" FROM platform.platform_invitations"));
    }

    // The release's migration job, told what the deployment tells it.
    private Task<(int ExitCode, string Output)> ReleaseAsync(OwnDatabase database, string firstOperator, bool withAdminHost = true)
    {
        var settings = new Dictionary<string, string> { ["Platform__FirstOperator"] = firstOperator };

        if (withAdminHost)
        {
            settings["Frontends__AdminHost"] = AdminHost;
        }

        return ApiCommand.RunAsync(factory, database, ["migrate"], settings);
    }

    // The app the release goes on to serve, on the same database.
    private WebApplicationFactory<Program> Serving(OwnDatabase database) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:ShopForge", database.ConnectionString));

    private static Task<List<string>> LettersToAsync(OwnDatabase database, string address) =>
        database.TextAsync($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id IS NULL AND payload LIKE {"%" + address + "%"} ORDER BY created_at");

    // The link as the newest letter carries it, read out of the outbox the running app delivers from.
    private static async Task<string> InvitationLinkAsync(OwnDatabase database, string address)
    {
        var letter = (await LettersToAsync(database, address)).Last();
        var text = string.Join(' ', Strings(JsonDocument.Parse(letter).RootElement));

        return Link().Match(text).Value;
    }

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Strings(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        _ => [],
    };

    [GeneratedRegex(@"https://[^\s]+?token=[A-Za-z0-9%_\-]+")]
    private static partial Regex Link();
}
