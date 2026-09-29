using System.Net;
using System.Net.Http.Json;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Platform;

public sealed class PlatformTwoFactorTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Turning_it_on_takes_two_steps_and_signing_in_then_takes_two_as_well()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        using var operatorClient = await PlainClientAsync(account);

        var setup = await BeginAsync(operatorClient);
        var before = await MeAsync(operatorClient);
        using var confirmed = await ConfirmAsync(operatorClient, CodeFor(setup.Secret));
        var codes = await confirmed.Content.ReadFromJsonAsync<RecoveryCodesView>(CancellationToken);
        var after = await MeAsync(operatorClient);

        var attempt = await SignInAsync(account);
        using var finished = await CompleteAsync(attempt.Ticket, CodeFor(setup.Secret));
        var session = await finished.Content.ReadFromJsonAsync<SignInView>(CancellationToken);

        Assert.StartsWith("otpauth://totp/ShopForge%20Platform:", setup.EnrolmentUri, StringComparison.Ordinal);
        Assert.False(before.IsTwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(RecoveryCodes.Count, codes!.RecoveryCodes.Count);
        Assert.True(after.IsTwoFactorEnabled);
        Assert.True(attempt.TwoFactorRequired);
        Assert.Null(attempt.User);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        Assert.Equal(account.Email, session!.User!.Email);
    }

    // An operator sees every company, so the password alone must stop being enough (D-129).
    [Fact]
    public async Task The_password_on_its_own_no_longer_reaches_any_company()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        var secret = await EnrolledAsync(account);

        var attempt = await SignInAsync(account);
        using var halfWay = factory.CreateClient();
        using var tenants = await halfWay.GetAsync("/api/platform/tenants", CancellationToken);
        using var wrongCode = await CompleteAsync(attempt.Ticket, "000000");
        using var rightCode = await CompleteAsync(attempt.Ticket, CodeFor(secret));

        Assert.True(attempt.TwoFactorRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, tenants.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rightCode.StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_gets_somebody_in_once_and_then_never_again()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        using var operatorClient = await PlainClientAsync(account);
        var setup = await BeginAsync(operatorClient);
        using var confirmed = await ConfirmAsync(operatorClient, CodeFor(setup.Secret));
        var codes = (await confirmed.Content.ReadFromJsonAsync<RecoveryCodesView>(CancellationToken))!.RecoveryCodes;

        var first = await SignInAsync(account);
        using var used = await CompleteAsync(first.Ticket, codes[0]);
        var second = await SignInAsync(account);
        using var reused = await CompleteAsync(second.Ticket, codes[0]);
        using var another = await CompleteAsync(second.Ticket, codes[1]);

        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, another.StatusCode);
    }

    // The two domains protect their tickets under different purposes, so a half-finished sign-in cannot be
    // carried across from one to the other (D-129).
    [Fact]
    public async Task A_ticket_earned_at_a_company_is_not_a_ticket_here()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var staff = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        var staffSecret = await EnrolledStaffAsync(staff);

        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        var operatorSecret = await EnrolledAsync(account);

        var staffAttempt = await StaffSignInAsync(staff);
        var operatorAttempt = await SignInAsync(account);

        using var staffTicketHere = await CompleteAsync(staffAttempt.Ticket, CodeFor(staffSecret));
        using var operatorTicketThere = await CompleteStaffAsync(operatorAttempt.Ticket, CodeFor(operatorSecret));

        Assert.Equal(HttpStatusCode.Unauthorized, staffTicketHere.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, operatorTicketThere.StatusCode);
    }

    [Fact]
    public async Task A_ticket_is_no_use_to_anybody_who_did_not_earn_it()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        var secret = await EnrolledAsync(account);
        var attempt = await SignInAsync(account);

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
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        var secret = await EnrolledAsync(account);
        var attempt = await SignInAsync(account);

        using var signedIn = await SignedInClientAsync(account, secret);
        using var changed = await signedIn.PostAsJsonAsync(
            "/api/platform/account/password",
            new { CurrentPassword = account.Password, NewPassword = "Chosen-again-2026" },
            CancellationToken);

        using var stale = await CompleteAsync(attempt.Ticket, CodeFor(secret));

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task Turning_it_off_needs_the_password()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        var secret = await EnrolledAsync(account);
        using var signedIn = await SignedInClientAsync(account, secret);

        using var guessed = await signedIn.PostAsJsonAsync("/api/platform/account/two-factor/off", new { Password = "not-theirs" }, CancellationToken);
        var stillOn = await MeAsync(signedIn);
        using var correct = await signedIn.PostAsJsonAsync("/api/platform/account/two-factor/off", new { account.Password }, CancellationToken);
        var afterwards = await MeAsync(signedIn);
        var plain = await SignInAsync(account);

        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.True(stillOn.IsTwoFactorEnabled);
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
        Assert.False(afterwards.IsTwoFactorEnabled);
        Assert.False(plain.TwoFactorRequired);
    }

    // Without the confirming step an operator could turn on a factor they cannot produce and shut themselves
    // out of the platform (D-128).
    [Fact]
    public async Task A_code_that_is_not_right_does_not_finish_the_enrolment()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        using var operatorClient = await PlainClientAsync(account);
        var setup = await BeginAsync(operatorClient);

        using var wrong = await ConfirmAsync(operatorClient, "000000");
        var afterwards = await MeAsync(operatorClient);
        var attempt = await SignInAsync(account);
        using var right = await ConfirmAsync(operatorClient, CodeFor(setup.Secret));

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.False(afterwards.IsTwoFactorEnabled);
        Assert.False(attempt.TwoFactorRequired);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task An_enrolment_nobody_finished_leaves_signing_in_alone()
    {
        var account = await TestPlatformUsers.CreateAsync(factory, CancellationToken);
        using var operatorClient = await PlainClientAsync(account);

        await BeginAsync(operatorClient);
        var attempt = await SignInAsync(account);

        Assert.False(attempt.TwoFactorRequired);
        Assert.NotNull(attempt.User);
    }

    private static string CodeFor(string secret) => Totp.CodeAt(secret, DateTimeOffset.UtcNow);

    private async Task<HttpClient> PlainClientAsync(TestOperator account)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/platform/auth/login", new { account.Email, account.Password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }

    private async Task<SetupView> BeginAsync(HttpClient operatorClient)
    {
        using var response = await operatorClient.PostAsync("/api/platform/account/two-factor", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SetupView>(CancellationToken))!;
    }

    private Task<HttpResponseMessage> ConfirmAsync(HttpClient operatorClient, string code) =>
        operatorClient.PostAsJsonAsync("/api/platform/account/two-factor/confirm", new { Code = code }, CancellationToken);

    private async Task<string> EnrolledAsync(TestOperator account)
    {
        using var operatorClient = await PlainClientAsync(account);
        var setup = await BeginAsync(operatorClient);
        using var confirmed = await ConfirmAsync(operatorClient, CodeFor(setup.Secret));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        return setup.Secret;
    }

    private async Task<SignInView> SignInAsync(TestOperator account)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/platform/auth/login", new { account.Email, account.Password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SignInView>(CancellationToken))!;
    }

    private async Task<HttpResponseMessage> CompleteAsync(string? ticket, string code)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/platform/auth/two-factor", new { Ticket = ticket, Code = code }, CancellationToken);
    }

    private async Task<HttpClient> SignedInClientAsync(TestOperator account, string secret)
    {
        var client = factory.CreateClient();
        using var started = await client.PostAsJsonAsync("/api/platform/auth/login", new { account.Email, account.Password }, CancellationToken);
        var attempt = (await started.Content.ReadFromJsonAsync<SignInView>(CancellationToken))!;
        using var finished = await client.PostAsJsonAsync(
            "/api/platform/auth/two-factor", new { attempt.Ticket, Code = CodeFor(secret) }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);

        return client;
    }

    private async Task<MeView> MeAsync(HttpClient operatorClient) =>
        (await operatorClient.GetFromJsonAsync<MeView>("/api/platform/auth/me", CancellationToken))!;

    private async Task<string> EnrolledStaffAsync(TestUser staff)
    {
        using var admin = await TestUsers.LoginAsync(factory, staff);
        using var began = await admin.PostAsync("/api/admin/account/two-factor", null, CancellationToken);
        var setup = (await began.Content.ReadFromJsonAsync<SetupView>(CancellationToken))!;
        using var confirmed = await admin.PostAsJsonAsync(
            "/api/admin/account/two-factor/confirm", new { Code = CodeFor(setup.Secret) }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        return setup.Secret;
    }

    private async Task<SignInView> StaffSignInAsync(TestUser staff)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { staff.Email, staff.Password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SignInView>(CancellationToken))!;
    }

    private async Task<HttpResponseMessage> CompleteStaffAsync(string? ticket, string code)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/admin/auth/two-factor", new { Ticket = ticket, Code = code }, CancellationToken);
    }

    private sealed record SetupView(string Secret, string EnrolmentUri);

    private sealed record RecoveryCodesView(List<string> RecoveryCodes);

    private sealed record SignInView(bool TwoFactorRequired, string? Ticket, MeView? User);

    private sealed record MeView(Guid Id, string Email, bool IsTwoFactorEnabled);
}
