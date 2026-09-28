using System.Net;
using System.Net.Http.Json;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Access;

public sealed class ColleagueTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invitation_makes_a_colleague_who_can_sign_in()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await SignInAsync(store.TenantId);
        var email = Address();

        var invitation = await InviteAsync(owner, email, TenantRole.Support);
        var token = await TokenAsync(email);
        using var looked = await owner.PostAsJsonAsync("/api/admin/invitations/details", new { Token = token }, CancellationToken);
        var open = await looked.Content.ReadFromJsonAsync<OpenInvitationView>(CancellationToken);
        using var accepted = await owner.PostAsJsonAsync(
            "/api/admin/invitations/accept", new { Token = token, Password = "Joins-the-company-2026" }, CancellationToken);

        using var colleague = factory.CreateClient();
        using var signedIn = await colleague.PostAsJsonAsync(
            "/api/admin/auth/login", new { Email = email, Password = "Joins-the-company-2026" }, CancellationToken);
        var colleagues = await ColleaguesAsync(owner);

        Assert.Equal((email, "Support"), (invitation.Email, invitation.Role));
        Assert.Equal((email, "Support"), (open!.Email, open.Role));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Contains(colleagues.Users, user => user.Email == email && user is { Role: "Support", IsActive: true });
        Assert.DoesNotContain(colleagues.Invitations, pending => pending.Email == email);
    }

    [Fact]
    public async Task An_invitation_works_once_and_a_withdrawn_one_never_does()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await SignInAsync(store.TenantId);
        var accepts = Address();
        var withdrawn = Address();

        await InviteAsync(owner, accepts, TenantRole.Warehouse);
        var token = await TokenAsync(accepts);
        using var first = await AcceptAsync(token);
        using var second = await AcceptAsync(token);

        var pending = await InviteAsync(owner, withdrawn, TenantRole.Warehouse);
        var withdrawnToken = await TokenAsync(withdrawn);
        using var gone = await owner.DeleteAsync($"/api/admin/users/invitations/{pending.Id}", CancellationToken);
        using var afterwards = await AcceptAsync(withdrawnToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    // An admin runs the shop but does not hand out the keys to the company.
    [Fact]
    public async Task Only_an_owner_makes_or_changes_an_owner()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await SignInAsync(store.TenantId);
        using var admin = await SignInAsync(store.TenantId, TenantRole.Admin);
        var ownerId = (await ColleaguesAsync(admin)).Users.Single(user => user.Role == "Owner").Id;

        using var invitesAnOwner = await admin.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = Address(), Role = "Owner" }, CancellationToken);
        using var invitesSupport = await admin.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = Address(), Role = "Support" }, CancellationToken);
        using var demotesTheOwner = await admin.PutAsJsonAsync(
            $"/api/admin/users/{ownerId}/role", new { Role = "Support" }, CancellationToken);
        using var turnsTheOwnerOff = await admin.PostAsync($"/api/admin/users/{ownerId}/deactivate", null, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, invitesAnOwner.StatusCode);
        Assert.Equal(HttpStatusCode.OK, invitesSupport.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, demotesTheOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, turnsTheOwnerOff.StatusCode);
    }

    // With this and the rule above, the last owner cannot be removed: it would take another owner to do it.
    [Fact]
    public async Task Nobody_changes_their_own_role_or_turns_themselves_off()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var owner = await TestUsers.LoginAsync(factory, user);
        var me = (await ColleaguesAsync(owner)).Users.Single(candidate => candidate.Email == user.Email).Id;

        using var demotesSelf = await owner.PutAsJsonAsync($"/api/admin/users/{me}/role", new { Role = "Support" }, CancellationToken);
        using var turnsSelfOff = await owner.PostAsync($"/api/admin/users/{me}/deactivate", null, CancellationToken);
        using var stillWorks = await owner.GetAsync("/api/admin/users", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, demotesSelf.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, turnsSelfOff.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task A_colleague_turned_off_is_out_at_once_and_can_be_let_back_in()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await SignInAsync(store.TenantId);
        var leaving = await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.Support);
        using var theirSession = await TestUsers.LoginAsync(factory, leaving);
        var id = (await ColleaguesAsync(owner)).Users.Single(user => user.Email == leaving.Email).Id;

        using var off = await owner.PostAsync($"/api/admin/users/{id}/deactivate", null, CancellationToken);
        using var sessionAfterwards = await theirSession.GetAsync("/api/admin/stores", CancellationToken);
        using var signsInAgain = await SignInAttemptAsync(leaving);
        var listed = (await ColleaguesAsync(owner)).Users.Single(user => user.Email == leaving.Email);

        using var backOn = await owner.PostAsync($"/api/admin/users/{id}/activate", null, CancellationToken);
        using var signsIn = await SignInAttemptAsync(leaving);

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signsInAgain.StatusCode);
        Assert.False(listed.IsActive);
        Assert.Equal(HttpStatusCode.OK, backOn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signsIn.StatusCode);
    }

    [Fact]
    public async Task One_company_neither_sees_nor_touches_another_company_s_people()
    {
        var (mine, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (theirs, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var me = await SignInAsync(mine.TenantId);
        using var them = await SignInAsync(theirs.TenantId);
        var theirEmail = Address();
        await InviteAsync(them, theirEmail, TenantRole.Support);
        var theirOwnerId = (await ColleaguesAsync(them)).Users.Single(user => user.Role == "Owner").Id;

        var seen = await ColleaguesAsync(me);
        using var demotes = await me.PutAsJsonAsync($"/api/admin/users/{theirOwnerId}/role", new { Role = "Support" }, CancellationToken);
        using var turnsOff = await me.PostAsync($"/api/admin/users/{theirOwnerId}/deactivate", null, CancellationToken);
        using var withdraws = await me.DeleteAsync(
            $"/api/admin/users/invitations/{(await ColleaguesAsync(them)).Invitations.Single(pending => pending.Email == theirEmail).Id}",
            CancellationToken);

        Assert.DoesNotContain(seen.Users, user => user.Id == theirOwnerId);
        Assert.DoesNotContain(seen.Invitations, pending => pending.Email == theirEmail);
        Assert.Equal(HttpStatusCode.NotFound, demotes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, turnsOff.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraws.StatusCode);
    }

    // One address, one company: it is what somebody signs in with.
    [Fact]
    public async Task An_address_that_already_works_on_ShopForge_cannot_be_invited()
    {
        var (mine, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (theirs, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var me = await SignInAsync(mine.TenantId);
        var elsewhere = await TestUsers.CreateAsync(factory.Services, theirs.TenantId);

        using var response = await me.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = elsewhere.Email, Role = "Support" }, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_invitation_needs_an_address_and_a_role_that_exist()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await SignInAsync(store.TenantId);

        using var response = await owner.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = "not-an-address", Role = "Sovereign" }, CancellationToken);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role", problem, StringComparison.OrdinalIgnoreCase);
    }

    private static string Address() => $"colleague-{Guid.NewGuid():N}@example.test";

    private async Task<HttpClient> SignInAsync(Guid tenantId, TenantRole role = TenantRole.Owner) =>
        await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, tenantId, role));

    private async Task<HttpResponseMessage> SignInAttemptAsync(TestUser user)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/admin/auth/login", new { user.Email, user.Password }, CancellationToken);
    }

    private async Task<HttpResponseMessage> AcceptAsync(string? token)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync(
            "/api/admin/invitations/accept", new { Token = token, Password = "Joins-the-company-2026" }, CancellationToken);
    }

    private async Task<InvitationView> InviteAsync(HttpClient admin, string email, TenantRole role)
    {
        using var response = await admin.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = email, Role = role.ToString() }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<InvitationView>(CancellationToken))!;
    }

    private Task<ColleaguesView> ColleaguesAsync(HttpClient admin) =>
        admin.GetFromJsonAsync<ColleaguesView>("/api/admin/users", CancellationToken)!;

    // The invitation travels as an outbox message with no store behind it, so the link only exists once the worker
    // has delivered it (D-111).
    private async Task<string?> TokenAsync(string email) =>
        await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);

    private sealed record ColleaguesView(List<ColleagueView> Users, List<InvitationView> Invitations);

    private sealed record ColleagueView(Guid Id, string Email, string Role, bool IsActive);

    private sealed record InvitationView(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

    private sealed record OpenInvitationView(string Email, string Role);
}
