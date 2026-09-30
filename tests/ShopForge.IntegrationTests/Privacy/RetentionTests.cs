using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Infrastructure.Messaging;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Privacy;

public sealed class RetentionTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The gap 18a left open: a delivered message keeps whatever the event carried, so an erased customer's
    // address lives on in the outbox until the sweep takes it (D-117, D-118).
    [Fact]
    public async Task A_delivered_message_is_swept_and_takes_an_erased_address_with_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Rea", LastName = "Viewer", Phone = (string?)null });
        await factory.EventuallyAsync(() => MessagesForAsync(email), count => count > 0, CancellationToken);

        var beforeTheSweep = await MessagesForAsync(email);
        await BackdateOutboxAsync(email);
        var removed = await SweepAsync();
        var afterTheSweep = await MessagesForAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.True(beforeTheSweep > 0);
        Assert.True(removed > 0);
        Assert.Equal(0, afterTheSweep);
    }

    // A message that gave up is waiting for a person, not for a sweep (D-068).
    [Fact]
    public async Task A_message_that_gave_up_is_left_where_somebody_can_see_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        factory.EmailDelivery.FailFor(email);

        try
        {
            using var registered = await new StorefrontApi(factory, furniture.Store).PostAsync(
                "/api/storefront/account/register",
                new { Email = email, Password = "Shop-forge-2026", FirstName = "Rea", LastName = "Viewer", Phone = (string?)null });
            Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

            for (var run = 0; run < 10 && await FailedForAsync(email) == 0; run++)
            {
                await factory.DispatchOutboxAsync(CancellationToken);
                await MakeEverythingDueAsync(email);
            }
        }
        finally
        {
            factory.EmailDelivery.StopFailingFor(email);
        }

        await BackdateOutboxAsync(email);
        await SweepAsync();

        Assert.Equal(1, await FailedForAsync(email));
    }

    [Fact]
    public async Task An_audit_entry_past_its_year_is_swept_and_a_recent_one_is_not()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var unpublished = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/unpublish", null, CancellationToken);
        var old = await BackdateAuditEntryAsync(furniture.VariantIds["oak-chair"].ToString(), days: 400);

        var removed = await SweepAsync();
        var entries = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?pageSize=100", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        Assert.True(removed > 0);
        Assert.DoesNotContain(entries!, entry => entry.Id == old);
        Assert.Contains(entries!, entry => entry.Action == "store.unpublished");
    }

    [Fact]
    public async Task An_invitation_nobody_took_up_does_not_stay_for_ever()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var stale = $"colleague-{Guid.NewGuid():N}@example.test";
        var fresh = $"colleague-{Guid.NewGuid():N}@example.test";
        await InviteAsync(owner, stale);
        await InviteAsync(owner, fresh);
        await BackdateInvitationAsync(stale);

        await SweepAsync();
        var colleagues = await owner.GetFromJsonAsync<ColleaguesView>("/api/admin/users", CancellationToken);

        Assert.DoesNotContain(colleagues!.Invitations, invitation => invitation.Email == stale);
        Assert.Contains(colleagues.Invitations, invitation => invitation.Email == fresh);
        Assert.Equal(0, await InvitationCountAsync(stale));
        Assert.Equal(1, await InvitationCountAsync(fresh));
    }

    [Fact]
    public async Task A_spent_operator_link_does_not_stay_for_ever()
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var stale = $"operator-{Guid.NewGuid():N}@shopforge.test";
        using var invited = await operatorClient.PostAsJsonAsync("/api/platform/operators/invitations", new { Email = stale }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

        var before = await PlatformInvitationCountAsync(stale);
        await BackdatePlatformInvitationAsync(stale);
        await SweepAsync();

        Assert.Equal(1, before);
        Assert.Equal(0, await PlatformInvitationCountAsync(stale));
    }

    private Task<int> SweepAsync() => factory.Services.GetRequiredService<StoreMaintenance>().RunAsync(CancellationToken);

    private async Task InviteAsync(HttpClient owner, string email)
    {
        using var response = await owner.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = email, Role = nameof(TenantRole.Support) }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<int> MessagesForAsync(string email) => ScalarAsync(
        $"SELECT count(*)::int AS \"Value\" FROM messaging.outbox_messages WHERE payload LIKE {'%' + email + '%'}");

    private Task<int> FailedForAsync(string email) => ScalarAsync(
        $"SELECT count(*)::int AS \"Value\" FROM messaging.outbox_messages WHERE status = 'Failed' AND payload LIKE {'%' + email + '%'}");

    private Task<int> InvitationCountAsync(string email) => ScalarAsync(
        $"SELECT count(*)::int AS \"Value\" FROM access.tenant_invitations WHERE email = {email}");

    private Task<int> PlatformInvitationCountAsync(string email) => ScalarAsync(
        $"SELECT count(*)::int AS \"Value\" FROM platform.platform_invitations WHERE email = {email}");

    private Task MakeEverythingDueAsync(string email) => ExecuteAsync(
        $"UPDATE messaging.outbox_messages SET due_at = now() WHERE status = 'Pending' AND payload LIKE {'%' + email + '%'}");

    private Task BackdateOutboxAsync(string email) => ExecuteAsync(
        $"UPDATE messaging.outbox_messages SET processed_at = now() - interval '40 days' WHERE payload LIKE {'%' + email + '%'}");

    private Task BackdateInvitationAsync(string email) => ExecuteAsync(
        $"UPDATE access.tenant_invitations SET expires_at = now() - interval '40 days' WHERE email = {email}");

    private Task BackdatePlatformInvitationAsync(string email) => ExecuteAsync(
        $"UPDATE platform.platform_invitations SET expires_at = now() - interval '40 days' WHERE email = {email}");

    // Backdates one entry the test is not about, so the entry it asserts on is the one that has to survive.
    // Stock belongs to the company rather than to one of its shops, so its entries carry no store at all.
    private async Task<Guid> BackdateAuditEntryAsync(string subject, int days)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var id = await dbContext.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM auditing.audit_entries WHERE subject = {subject} LIMIT 1")
            .SingleAsync(CancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE auditing.audit_entries SET recorded_at = now() - make_interval(days => {days}) WHERE id = {id}",
            CancellationToken);

        return id;
    }

    // Nothing is in scope, which is the point: these rows belong to no store.
    private async Task<int> ScalarAsync(FormattableString sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<DbContext>().Database.SqlQuery<int>(sql).SingleAsync(CancellationToken);
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<DbContext>().Database.ExecuteSqlAsync(sql, CancellationToken);
    }

    private sealed record AuditView(Guid Id, string Action);

    private sealed record ColleaguesView(List<InvitationView> Invitations);

    private sealed record InvitationView(Guid Id, string Email);
}
