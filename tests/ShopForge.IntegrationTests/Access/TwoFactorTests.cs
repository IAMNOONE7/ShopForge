using System.Net;
using System.Net.Http.Json;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Access;

public sealed class TwoFactorTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Turning_it_on_takes_two_steps_and_signing_in_then_takes_two_as_well()
    {
        var user = await UserAsync();
        using var admin = await TestUsers.LoginAsync(factory, user);

        var setup = await BeginAsync(admin);
        var before = await MeAsync(admin);
        using var confirmed = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = CodeFor(setup.Secret) }, CancellationToken);
        var codes = await confirmed.Content.ReadFromJsonAsync<RecoveryCodesView>(CancellationToken);
        var after = await MeAsync(admin);

        // Signing in stops half-way now.
        var attempt = await SignInAsync(user);
        using var finished = await CompleteAsync(attempt.Ticket, CodeFor(setup.Secret));
        var session = await finished.Content.ReadFromJsonAsync<SignInView>(CancellationToken);

        Assert.StartsWith("otpauth://totp/ShopForge:", setup.EnrolmentUri, StringComparison.Ordinal);
        Assert.False(before.IsTwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(RecoveryCodes.Count, codes!.RecoveryCodes.Count);
        Assert.True(after.IsTwoFactorEnabled);
        Assert.True(attempt.TwoFactorRequired);
        Assert.Null(attempt.User);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        Assert.Equal(user.Email, session!.User!.Email);
    }

    // The password alone must stop being enough, which is the entire point.
    [Fact]
    public async Task The_password_on_its_own_no_longer_opens_anything()
    {
        var user = await UserAsync();
        var secret = await EnrolledAsync(user);

        var attempt = await SignInAsync(user);
        using var withTheTicketAlone = factory.CreateClient();
        using var anything = await withTheTicketAlone.GetAsync("/api/admin/stores", CancellationToken);
        using var wrongCode = await CompleteAsync(attempt.Ticket, "000000");
        using var rightCode = await CompleteAsync(attempt.Ticket, CodeFor(secret));

        Assert.True(attempt.TwoFactorRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, anything.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rightCode.StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_gets_somebody_in_once_and_then_never_again()
    {
        var user = await UserAsync();
        using var admin = await TestUsers.LoginAsync(factory, user);
        var setup = await BeginAsync(admin);
        using var confirmed = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = CodeFor(setup.Secret) }, CancellationToken);
        var codes = (await confirmed.Content.ReadFromJsonAsync<RecoveryCodesView>(CancellationToken))!.RecoveryCodes;

        var first = await SignInAsync(user);
        using var used = await CompleteAsync(first.Ticket, codes[0]);
        var second = await SignInAsync(user);
        using var reused = await CompleteAsync(second.Ticket, codes[0]);
        using var another = await CompleteAsync(second.Ticket, codes[1]);

        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, another.StatusCode);
    }

    [Fact]
    public async Task A_ticket_is_no_use_to_anybody_who_did_not_earn_it()
    {
        var user = await UserAsync();
        var secret = await EnrolledAsync(user);
        var attempt = await SignInAsync(user);

        using var invented = await CompleteAsync("not-a-real-ticket", CodeFor(secret));
        using var empty = await CompleteAsync(null, CodeFor(secret));
        using var tampered = await CompleteAsync(attempt.Ticket![..^4] + "AAAA", CodeFor(secret));

        Assert.Equal(HttpStatusCode.Unauthorized, invented.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
    }

    // A password changed between the two steps means the first step no longer stands (D-113).
    [Fact]
    public async Task A_ticket_stops_working_when_the_password_behind_it_changes()
    {
        var user = await UserAsync();
        var secret = await EnrolledAsync(user);
        var attempt = await SignInAsync(user);

        using var theirSession = await CompleteAsync(attempt.Ticket, CodeFor(secret));
        using var signedIn = await SignedInClientAsync(user, secret);
        using var changed = await signedIn.PostAsJsonAsync(
            "/api/admin/account/password",
            new { CurrentPassword = user.Password, NewPassword = "Chosen-again-2026" },
            CancellationToken);

        using var stale = await CompleteAsync(attempt.Ticket, CodeFor(secret));

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task Turning_it_off_needs_the_password()
    {
        var user = await UserAsync();
        var secret = await EnrolledAsync(user);
        using var signedIn = await SignedInClientAsync(user, secret);

        using var guessed = await signedIn.PostAsJsonAsync("/api/admin/account/two-factor/off", new { Password = "not-theirs" }, CancellationToken);
        var stillOn = await MeAsync(signedIn);
        using var correct = await signedIn.PostAsJsonAsync("/api/admin/account/two-factor/off", new { user.Password }, CancellationToken);
        var afterwards = await MeAsync(signedIn);
        var plain = await SignInAsync(user);

        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.True(stillOn.IsTwoFactorEnabled);
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
        Assert.False(afterwards.IsTwoFactorEnabled);
        Assert.False(plain.TwoFactorRequired);
    }

    // Confirming is what proves the authenticator was really set up. Without that proof somebody could turn on a
    // factor they cannot produce and lock themselves out of the company (D-128).
    [Fact]
    public async Task A_code_that_is_not_right_does_not_finish_the_enrolment()
    {
        var user = await UserAsync();
        using var admin = await TestUsers.LoginAsync(factory, user);
        var setup = await BeginAsync(admin);

        using var wrong = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = "000000" }, CancellationToken);
        var afterwards = await MeAsync(admin);
        var attempt = await SignInAsync(user);

        using var right = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = CodeFor(setup.Secret) }, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.False(afterwards.IsTwoFactorEnabled);
        Assert.False(attempt.TwoFactorRequired);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task An_enrolment_nobody_finished_leaves_signing_in_alone()
    {
        var user = await UserAsync();
        using var admin = await TestUsers.LoginAsync(factory, user);

        await BeginAsync(admin);
        var attempt = await SignInAsync(user);

        Assert.False(attempt.TwoFactorRequired);
        Assert.NotNull(attempt.User);
    }

    private async Task<TestUser> UserAsync()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        return await TestUsers.CreateAsync(factory.Services, store.TenantId);
    }

    private static string CodeFor(string secret) => Totp.CodeAt(secret, DateTimeOffset.UtcNow);

    private async Task<SetupView> BeginAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync("/api/admin/account/two-factor", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SetupView>(CancellationToken))!;
    }

    private async Task<string> EnrolledAsync(TestUser user)
    {
        using var admin = await TestUsers.LoginAsync(factory, user);
        var setup = await BeginAsync(admin);
        using var confirmed = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = CodeFor(setup.Secret) }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        return setup.Secret;
    }

    private async Task<SignInView> SignInAsync(TestUser user)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login", new { user.Email, user.Password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SignInView>(CancellationToken))!;
    }

    private async Task<HttpResponseMessage> CompleteAsync(string? ticket, string code)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/admin/auth/two-factor", new { Ticket = ticket, Code = code }, CancellationToken);
    }

    private async Task<HttpClient> SignedInClientAsync(TestUser user, string secret)
    {
        var client = factory.CreateClient();
        using var started = await client.PostAsJsonAsync("/api/admin/auth/login", new { user.Email, user.Password }, CancellationToken);
        var attempt = (await started.Content.ReadFromJsonAsync<SignInView>(CancellationToken))!;
        using var finished = await client.PostAsJsonAsync(
            "/api/admin/auth/two-factor", new { attempt.Ticket, Code = CodeFor(secret) }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);

        return client;
    }

    private async Task<MeView> MeAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<MeView>("/api/admin/auth/me", CancellationToken))!;

    private sealed record SetupView(string Secret, string EnrolmentUri);

    private sealed record RecoveryCodesView(List<string> RecoveryCodes);

    private sealed record SignInView(bool TwoFactorRequired, string? Ticket, MeView? User);

    private sealed record MeView(Guid Id, string Email, string Role, Guid TenantId, bool IsTwoFactorEnabled);
}
