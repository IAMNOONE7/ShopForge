using System.Net;
using System.Net.Http.Json;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Access;

public sealed class StaffPasswordTests(ShopForgeApiFactory factory)
{
    private const string NewPassword = "Chosen-again-2026";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changing_your_password_keeps_this_browser_and_ends_the_others()
    {
        var user = await UserAsync();
        using var here = await TestUsers.LoginAsync(factory, user);
        using var elsewhere = await TestUsers.LoginAsync(factory, user);

        using var changed = await here.PostAsJsonAsync(
            "/api/admin/account/password",
            new { CurrentPassword = user.Password, NewPassword },
            CancellationToken);
        using var stillHere = await here.GetAsync("/api/admin/users", CancellationToken);
        using var theOtherOne = await elsewhere.GetAsync("/api/admin/users", CancellationToken);
        using var withTheOldPassword = await SignInAsync(user.Email, user.Password);
        using var withTheNewOne = await SignInAsync(user.Email, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theOtherOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
    }

    [Fact]
    public async Task A_password_is_changed_only_by_somebody_who_knows_the_current_one()
    {
        var user = await UserAsync();
        using var admin = await TestUsers.LoginAsync(factory, user);

        using var guessed = await admin.PostAsJsonAsync(
            "/api/admin/account/password",
            new { CurrentPassword = "Not-their-password-1", NewPassword },
            CancellationToken);
        using var tooShort = await admin.PostAsJsonAsync(
            "/api/admin/account/password",
            new { CurrentPassword = user.Password, NewPassword = "short" },
            CancellationToken);
        using var unchanged = await SignInAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
    }

    [Fact]
    public async Task Signing_out_everywhere_leaves_only_the_browser_that_asked()
    {
        var user = await UserAsync();
        using var here = await TestUsers.LoginAsync(factory, user);
        using var phone = await TestUsers.LoginAsync(factory, user);
        using var laptop = await TestUsers.LoginAsync(factory, user);

        using var asked = await here.PostAsync("/api/admin/account/sign-out-everywhere", null, CancellationToken);
        using var stillHere = await here.GetAsync("/api/admin/users", CancellationToken);
        using var onThePhone = await phone.GetAsync("/api/admin/users", CancellationToken);
        using var onTheLaptop = await laptop.GetAsync("/api/admin/users", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, asked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onThePhone.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onTheLaptop.StatusCode);
    }

    [Fact]
    public async Task A_forgotten_password_is_reset_by_e_mail_and_takes_every_session_with_it()
    {
        var user = await UserAsync();
        using var openSession = await TestUsers.LoginAsync(factory, user);

        using var asked = await AnonymousPostAsync("/api/admin/auth/password/forgot", new { user.Email });
        var token = await TokenAsync(user.Email);
        using var reset = await AnonymousPostAsync("/api/admin/auth/password/reset", new { Token = token, Password = NewPassword });

        using var theOldSession = await openSession.GetAsync("/api/admin/users", CancellationToken);
        using var withTheOldPassword = await SignInAsync(user.Email, user.Password);
        using var withTheNewOne = await SignInAsync(user.Email, NewPassword);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theOldSession.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
    }

    [Fact]
    public async Task A_reset_link_works_once_and_the_newest_one_is_the_only_one()
    {
        var user = await UserAsync();

        await AnonymousPostAsync("/api/admin/auth/password/forgot", new { user.Email });
        var first = await TokenAsync(user.Email);
        await AnonymousPostAsync("/api/admin/auth/password/forgot", new { user.Email });
        var second = await TokenAsync(user.Email);

        using var theOldLink = await AnonymousPostAsync("/api/admin/auth/password/reset", new { Token = first, Password = NewPassword });
        using var theNewOne = await AnonymousPostAsync("/api/admin/auth/password/reset", new { Token = second, Password = NewPassword });
        using var reused = await AnonymousPostAsync("/api/admin/auth/password/reset", new { Token = second, Password = "Taken-over-2026" });
        using var withTheChosenOne = await SignInAsync(user.Email, NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, theOldLink.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, theNewOne.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTheChosenOne.StatusCode);
    }

    // Asking about an address says nothing about whether it works here.
    [Fact]
    public async Task Asking_about_an_address_nobody_uses_looks_the_same_and_sends_nothing()
    {
        var stranger = $"nobody-{Guid.NewGuid():N}@example.test";

        using var asked = await AnonymousPostAsync("/api/admin/auth/password/forgot", new { Email = stranger });
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Empty(factory.Emails.For(stranger));
    }

    [Fact]
    public async Task A_link_sent_to_somebody_else_is_no_use()
    {
        var mine = await UserAsync();
        var theirs = await UserAsync();
        await AnonymousPostAsync("/api/admin/auth/password/forgot", new { Email = theirs.Email });
        var theirToken = await TokenAsync(theirs.Email);

        using var invented = await AnonymousPostAsync(
            "/api/admin/auth/password/reset", new { Token = Guid.NewGuid().ToString("N"), Password = NewPassword });
        using var used = await AnonymousPostAsync("/api/admin/auth/password/reset", new { Token = theirToken, Password = NewPassword });
        using var mineIsUntouched = await SignInAsync(mine.Email, mine.Password);
        using var theirsChanged = await SignInAsync(theirs.Email, NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, invented.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, used.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mineIsUntouched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, theirsChanged.StatusCode);
    }

    private async Task<TestUser> UserAsync()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        return await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.Admin);
    }

    private async Task<HttpResponseMessage> SignInAsync(string email, string password)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/admin/auth/login", new { Email = email, Password = password }, CancellationToken);
    }

    private async Task<HttpResponseMessage> AnonymousPostAsync(string path, object body)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync(path, body, CancellationToken);
    }

    // The mail is an outbox message with no store behind it, so the link exists once the worker has delivered it.
    private Task<string?> TokenAsync(string email) =>
        factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
}
