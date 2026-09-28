using System.Net;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Customers;

public sealed class EmailChangeTests(ShopForgeApiFactory factory)
{
    private const string Password = "Shop-forge-2026";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_moves_to_a_new_address_once_they_have_proved_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var (before, after) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(shopper, before);

        using var asked = await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password });
        var beforeConfirming = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");
        var token = await TokenAsync(after);
        using var confirmed = await shopper.PostAsync("/api/storefront/account/email/confirm", new { Token = token });
        var profile = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        using var withTheNewOne = await SignInAsync(furniture.Store, after);
        using var withTheOldOne = await SignInAsync(furniture.Store, before);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(before, beforeConfirming.Email);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(after, profile.Email);
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldOne.StatusCode);
    }

    // The link goes to the address being moved to; the old one is told nothing until the move is real.
    [Fact]
    public async Task The_link_goes_to_the_new_address_and_the_old_one_still_works_until_it_is_used()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var (before, after) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(shopper, before);
        var lettersBefore = factory.Emails.For(before).Count;

        await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password });
        await TokenAsync(after);

        using var stillSignsIn = await SignInAsync(furniture.Store, before);

        Assert.Equal(lettersBefore, factory.Emails.For(before).Count);
        Assert.Equal(HttpStatusCode.OK, stillSignsIn.StatusCode);
    }

    [Fact]
    public async Task Moving_needs_the_password_and_a_real_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var (before, after) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(shopper, before);

        using var guessed = await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password = "Not-the-password-1" });
        using var nonsense = await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = "not-an-address", Password });
        using var anonymous = new StorefrontApi(factory, furniture.Store);
        using var signedOut = await anonymous.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password });
        var profile = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nonsense.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(before, profile.Email);
    }

    // What the whole design is for: the stores of one company stay strangers to each other (D-102, D-115).
    [Fact]
    public async Task Moving_at_one_store_leaves_the_same_person_s_account_at_another_alone()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var here = new StorefrontApi(factory, furniture.Store);
        using var there = new StorefrontApi(factory, furniture.OtherStore);
        var (before, after) = (UniqueEmail(), UniqueEmail());
        const string otherPassword = "Another-store-2026";
        await RegisterAsync(here, before);
        await RegisterAsync(there, before, otherPassword);

        await here.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password });
        var token = await TokenAsync(after);
        using var confirmed = await here.PostAsync("/api/storefront/account/email/confirm", new { Token = token });

        using var hereWithTheNewOne = await SignInAsync(furniture.Store, after);
        using var thereWithTheOldOne = await SignInAsync(furniture.OtherStore, before, otherPassword);
        using var thereWithTheNewOne = await SignInAsync(furniture.OtherStore, after, otherPassword);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hereWithTheNewOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thereWithTheOldOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, thereWithTheNewOne.StatusCode);
    }

    [Fact]
    public async Task An_address_that_already_has_an_account_here_is_told_so_and_nothing_moves()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var mine = new StorefrontApi(factory, furniture.Store);
        using var theirs = new StorefrontApi(factory, furniture.Store);
        var (before, somebodyElse) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(mine, before);
        await RegisterAsync(theirs, somebodyElse);

        using var asked = await mine.PostAsync("/api/storefront/account/email", new { NewEmail = somebodyElse, Password });
        var subjects = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(somebodyElse).Select(message => message.Subject).ToList()),
            letters => letters.Any(subject => subject.Contains("already have", StringComparison.Ordinal)),
            CancellationToken);
        var profile = await mine.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        // The same answer as a move that is going ahead: the asker learns nothing about the other account.
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Contains(subjects, subject => subject.Contains("already have", StringComparison.Ordinal));
        Assert.Equal(before, profile.Email);
    }

    // Between asking and confirming, somebody else can register the address here.
    [Fact]
    public async Task An_address_taken_here_after_the_asking_is_refused_at_the_confirming()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var mine = new StorefrontApi(factory, furniture.Store);
        using var theirs = new StorefrontApi(factory, furniture.Store);
        var (before, wanted) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(mine, before);

        await mine.PostAsync("/api/storefront/account/email", new { NewEmail = wanted, Password });
        var token = await TokenAsync(wanted);
        await RegisterAsync(theirs, wanted);

        using var confirmed = await mine.PostAsync("/api/storefront/account/email/confirm", new { Token = token });
        var problem = await confirmed.Content.ReadAsStringAsync(CancellationToken);
        var profile = await mine.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        // Said in as many words, rather than left to the unique index to refuse as a bare conflict.
        Assert.Equal(HttpStatusCode.Conflict, confirmed.StatusCode);
        Assert.Contains("already has an account here", problem, StringComparison.Ordinal);
        Assert.Equal(before, profile.Email);
    }

    [Fact]
    public async Task A_confirmation_link_works_once_and_only_the_newest_one_works()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var (before, first, second) = (UniqueEmail(), UniqueEmail(), UniqueEmail());
        await RegisterAsync(shopper, before);

        await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = first, Password });
        var firstToken = await TokenAsync(first);
        await shopper.PostAsync("/api/storefront/account/email", new { NewEmail = second, Password });
        var secondToken = await TokenAsync(second);

        using var theAbandonedOne = await shopper.PostAsync("/api/storefront/account/email/confirm", new { Token = firstToken });
        using var theNewestOne = await shopper.PostAsync("/api/storefront/account/email/confirm", new { Token = secondToken });
        using var reused = await shopper.PostAsync("/api/storefront/account/email/confirm", new { Token = secondToken });
        var profile = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        Assert.Equal(HttpStatusCode.BadRequest, theAbandonedOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, theNewestOne.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(second, profile.Email);
    }

    [Fact]
    public async Task A_confirmation_link_from_one_store_does_not_move_an_account_at_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var here = new StorefrontApi(factory, furniture.Store);
        using var there = new StorefrontApi(factory, furniture.OtherStore);
        var (before, after) = (UniqueEmail(), UniqueEmail());
        await RegisterAsync(here, before);

        await here.PostAsync("/api/storefront/account/email", new { NewEmail = after, Password });
        var token = await TokenAsync(after);

        using var elsewhere = await there.PostAsync("/api/storefront/account/email/confirm", new { Token = token });
        using var atHome = await here.PostAsync("/api/storefront/account/email/confirm", new { Token = token });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, atHome.StatusCode);
    }

    [Fact]
    public async Task A_customer_who_lost_the_verification_e_mail_asks_for_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = UniqueEmail();
        using var registered = await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        var first = await TokenAsync(email);

        using var asked = await shopper.PostAsync("/api/storefront/account/verification/resend", new { Email = email });
        var second = await TokenAsync(email, notThisOne: first);

        using var theLostOne = await shopper.PostAsync("/api/storefront/account/verify", new { Token = first });
        using var theNewOne = await shopper.PostAsync("/api/storefront/account/verify", new { Token = second });

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, theLostOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, theNewOne.StatusCode);
    }

    [Fact]
    public async Task Asking_again_for_an_address_this_store_does_not_know_answers_the_same_and_sends_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var here = new StorefrontApi(factory, furniture.Store);
        using var there = new StorefrontApi(factory, furniture.OtherStore);
        var stranger = UniqueEmail();
        var elsewhere = UniqueEmail();
        await RegisterAsync(there, elsewhere);
        var lettersFromTheOtherStore = factory.Emails.For(elsewhere).Count;

        using var unknown = await here.PostAsync("/api/storefront/account/verification/resend", new { Email = stranger });
        using var knownElsewhere = await here.PostAsync("/api/storefront/account/verification/resend", new { Email = elsewhere });
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownElsewhere.StatusCode);
        Assert.Empty(factory.Emails.For(stranger));
        Assert.Equal(lettersFromTheOtherStore, factory.Emails.For(elsewhere).Count);
    }

    private static string UniqueEmail() => $"shopper-{Guid.NewGuid():N}@example.test";

    private static object Registration(string email, string password = Password) =>
        new { Email = email, Password = password, FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null };

    private async Task RegisterAsync(StorefrontApi shopper, string email, string password = Password)
    {
        using var registered = await shopper.PostAsync("/api/storefront/account/register", Registration(email, password));
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        using var verified = await shopper.PostAsync("/api/storefront/account/verify", new { Token = await TokenAsync(email) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    private async Task<HttpResponseMessage> SignInAsync(TestStore store, string email, string password = Password)
    {
        using var shopper = new StorefrontApi(factory, store);

        return await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = password });
    }

    private readonly Dictionary<string, string> _linksUsed = [];

    // An address here can be sent a verification link and then a change link, and the newest one delivered is not
    // always the newest one issued, so a test asks for a link it has not already used.
    private async Task<string?> TokenAsync(string email, string? notThisOne = null)
    {
        var link = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            candidate => candidate is not null
                && candidate != notThisOne
                && (!_linksUsed.TryGetValue(email, out var used) || candidate != used),
            CancellationToken);
        _linksUsed[email] = link!;

        return link;
    }

    private sealed record CustomerView(string Email, string FirstName, string LastName, string? Phone);
}
