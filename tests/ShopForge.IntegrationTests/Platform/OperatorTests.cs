using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Platform;

public sealed class OperatorTests(ShopForgeApiFactory factory)
{
    private const string Password = "Runs-the-platform-too-2026";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_operator_brings_in_another_one_who_chooses_their_own_password()
    {
        using var operatorClient = await SignInAsync();
        var email = Address();

        var invitation = await InviteAsync(operatorClient, email);
        var token = await TokenAsync(email);
        using var looked = await operatorClient.PostAsJsonAsync("/api/platform/invitations/details", new { Token = token }, CancellationToken);
        var open = await looked.Content.ReadFromJsonAsync<OpenInvitationView>(CancellationToken);
        using var accepted = await AcceptAsync(token);

        using var newcomer = factory.CreateClient();
        using var signedIn = await newcomer.PostAsJsonAsync(
            "/api/platform/auth/login", new { Email = email, Password }, CancellationToken);
        using var seesTenants = await newcomer.GetAsync("/api/platform/tenants", CancellationToken);
        var listed = await OperatorsAsync(operatorClient);

        Assert.Equal(email, invitation.Email);
        Assert.Equal(email, open!.Email);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, seesTenants.StatusCode);
        Assert.Contains(listed.Operators, candidate => candidate.Email == email && candidate.IsActive);
    }

    [Fact]
    public async Task An_operator_invitation_works_once_and_a_withdrawn_one_never_does()
    {
        using var operatorClient = await SignInAsync();
        var withdrawn = Address();
        var accepts = Address();

        await InviteAsync(operatorClient, accepts);
        var token = await TokenAsync(accepts);
        using var first = await AcceptAsync(token);
        using var second = await AcceptAsync(token);

        var pending = await InviteAsync(operatorClient, withdrawn);
        var withdrawnToken = await TokenAsync(withdrawn);
        using var gone = await operatorClient.DeleteAsync($"/api/platform/operators/invitations/{pending.Id}", CancellationToken);
        using var afterwards = await AcceptAsync(withdrawnToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task An_operator_whose_access_is_withdrawn_is_out_at_once()
    {
        using var operatorClient = await SignInAsync();
        var email = Address();
        await InviteAsync(operatorClient, email);
        using var theirs = await AcceptAndSignInAsync(await TokenAsync(email), email);
        var id = (await OperatorsAsync(operatorClient)).Operators.Single(candidate => candidate.Email == email).Id;

        using var off = await operatorClient.PostAsync($"/api/platform/operators/{id}/deactivate", null, CancellationToken);
        using var theirSession = await theirs.GetAsync("/api/platform/tenants", CancellationToken);
        using var signsInAgain = await SignInAttemptAsync(email);

        using var backOn = await operatorClient.PostAsync($"/api/platform/operators/{id}/activate", null, CancellationToken);
        using var signsIn = await SignInAttemptAsync(email);

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theirSession.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signsInAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, backOn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signsIn.StatusCode);
    }

    // Nobody can take the last operator away, because nobody can take themselves away.
    [Fact]
    public async Task An_operator_cannot_turn_their_own_account_off()
    {
        using var operatorClient = await SignInAsync();
        var me = await MeAsync(operatorClient);

        using var response = await operatorClient.PostAsync($"/api/platform/operators/{me.Id}/deactivate", null, CancellationToken);
        using var stillWorks = await operatorClient.GetAsync("/api/platform/tenants", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task An_address_that_already_runs_the_platform_cannot_be_invited()
    {
        using var operatorClient = await SignInAsync();
        var me = await MeAsync(operatorClient);

        using var response = await operatorClient.PostAsJsonAsync(
            "/api/platform/operators/invitations", new { Email = me.Email }, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changing_an_operator_password_keeps_this_browser_and_ends_the_others()
    {
        using var operatorClient = await SignInAsync();
        var email = Address();
        await InviteAsync(operatorClient, email);
        using var here = await AcceptAndSignInAsync(await TokenAsync(email), email);
        using var elsewhere = await SignInAsAsync(email, Password);

        using var changed = await here.PostAsJsonAsync(
            "/api/platform/account/password",
            new { CurrentPassword = Password, NewPassword = "Chosen-again-2026" },
            CancellationToken);
        using var stillHere = await here.GetAsync("/api/platform/tenants", CancellationToken);
        using var theOtherOne = await elsewhere.GetAsync("/api/platform/tenants", CancellationToken);
        using var withTheOldPassword = await SignInAttemptAsync(email);
        using var guessed = await here.PostAsJsonAsync(
            "/api/platform/account/password",
            new { CurrentPassword = "Not-the-password-1", NewPassword = "Another-one-2026" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theOtherOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
    }

    [Fact]
    public async Task An_operator_signs_out_everywhere_but_here()
    {
        using var operatorClient = await SignInAsync();
        var email = Address();
        await InviteAsync(operatorClient, email);
        using var here = await AcceptAndSignInAsync(await TokenAsync(email), email);
        using var phone = await SignInAsAsync(email, Password);

        using var asked = await here.PostAsync("/api/platform/account/sign-out-everywhere", null, CancellationToken);
        using var stillHere = await here.GetAsync("/api/platform/tenants", CancellationToken);
        using var onThePhone = await phone.GetAsync("/api/platform/tenants", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, asked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onThePhone.StatusCode);
    }

    [Fact]
    public async Task An_operator_who_has_forgotten_their_password_is_sent_a_way_back_in()
    {
        using var operatorClient = await SignInAsync();
        var email = Address();
        await InviteAsync(operatorClient, email);
        var invitationToken = await TokenAsync(email);
        using var theirSession = await AcceptAndSignInAsync(invitationToken, email);

        using var asked = await AnonymousPostAsync("/api/platform/auth/password/forgot", new { Email = email });
        var token = await TokenAsync(email);
        using var reset = await AnonymousPostAsync("/api/platform/auth/password/reset", new { Token = token, Password = "Chosen-again-2026" });
        using var reused = await AnonymousPostAsync("/api/platform/auth/password/reset", new { Token = token, Password = "Taken-over-2026" });

        using var theOldSession = await theirSession.GetAsync("/api/platform/tenants", CancellationToken);
        using var withTheNewOne = await AnonymousPostAsync(
            "/api/platform/auth/login", new { Email = email, Password = "Chosen-again-2026" });

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theOldSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
    }

    [Fact]
    public async Task Asking_about_an_address_that_runs_nothing_looks_the_same_and_sends_nothing()
    {
        var stranger = Address();

        using var asked = await AnonymousPostAsync("/api/platform/auth/password/forgot", new { Email = stranger });
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Empty(factory.Emails.For(stranger));
    }

    [Fact]
    public async Task Running_the_platform_is_not_something_a_company_s_staff_can_ask_for()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var tenantAdmin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        using var lists = await tenantAdmin.GetAsync("/api/platform/operators", CancellationToken);
        using var invites = await tenantAdmin.PostAsJsonAsync(
            "/api/platform/operators/invitations", new { Email = Address() }, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, lists.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invites.StatusCode);
    }

    private static string Address() => $"operator-{Guid.NewGuid():N}@shopforge.test";

    private Task<HttpClient> SignInAsync() => TestPlatformUsers.SignInAsync(factory, CancellationToken);

    private async Task<OperatorView> MeAsync(HttpClient operatorClient)
    {
        var me = await operatorClient.GetFromJsonAsync<OperatorView>("/api/platform/auth/me", CancellationToken);

        return me!;
    }

    private Task<OperatorsView> OperatorsAsync(HttpClient operatorClient) =>
        operatorClient.GetFromJsonAsync<OperatorsView>("/api/platform/operators", CancellationToken)!;

    private async Task<InvitationView> InviteAsync(HttpClient operatorClient, string email)
    {
        using var response = await operatorClient.PostAsJsonAsync(
            "/api/platform/operators/invitations", new { Email = email }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<InvitationView>(CancellationToken))!;
    }

    private async Task<HttpResponseMessage> AcceptAsync(string? token)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/platform/invitations/accept", new { Token = token, Password }, CancellationToken);
    }

    private async Task<HttpClient> AcceptAndSignInAsync(string? token, string email)
    {
        using (var accepted = await AcceptAsync(token))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        return await SignInAsAsync(email, Password);
    }

    private async Task<HttpClient> SignInAsAsync(string email, string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/platform/auth/login", new { Email = email, Password = password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }

    private async Task<HttpResponseMessage> SignInAttemptAsync(string email)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/platform/auth/login", new { Email = email, Password }, CancellationToken);
    }

    private async Task<HttpResponseMessage> AnonymousPostAsync(string path, object body)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync(path, body, CancellationToken);
    }

    private Task<string?> TokenAsync(string email) =>
        factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);

    private sealed record OperatorsView(List<OperatorView> Operators, List<InvitationView> Invitations);

    private sealed record OperatorView(Guid Id, string Email, bool IsActive);

    private sealed record InvitationView(Guid Id, string Email, DateTimeOffset ExpiresAt);

    private sealed record OpenInvitationView(string Email);
}
