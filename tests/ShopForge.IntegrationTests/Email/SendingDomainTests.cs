using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.IntegrationTests.Provisioning;
using ShopForge.Shared.Provisioning;

namespace ShopForge.IntegrationTests.Email;

// A store's own sending domain against a recorded Mailgun: connected as an operation, sent through with its own key,
// suppressed for its own store only, and removed again. Roadmap V2 V15's acceptance (D-211).
public sealed class SendingDomainTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_stores_send_from_their_own_domains_each_with_a_key_of_its_own()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var volt = await host.StoreWithProvedDomainAsync(factory);
        var woodOperation = await host.ConnectAndWaitAsync(wood, replyTo: $"pomoc@{wood.Domain}");
        var voltOperation = await host.ConnectAndWaitAsync(volt);
        var woodDone = await host.PublishAndFinishAsync(wood, woodOperation);
        var voltDone = await host.PublishAndFinishAsync(volt, voltOperation);

        await host.SendAsync(wood.Store, "buyer@example.test");
        await host.SendAsync(volt.Store, "buyer@example.test");

        var woodKey = Assert.Single(host.Mailgun.Keys, key => key.Domain == $"mail.{wood.Domain}");
        var voltKey = Assert.Single(host.Mailgun.Keys, key => key.Domain == $"mail.{volt.Domain}");
        var woodMail = Assert.Single(host.Mailgun.Sent, sent => sent.Domain == $"mail.{wood.Domain}");
        var voltMail = Assert.Single(host.Mailgun.Sent, sent => sent.Domain == $"mail.{volt.Domain}");

        Assert.Equal((OperationState.Succeeded, OperationState.Succeeded), (woodDone.State, voltDone.State));
        Assert.Equal((woodKey.Id, $"\"{wood.Store.Name}\" <obchod@{wood.Domain}>", $"pomoc@{wood.Domain}"), (woodMail.Key, woodMail.From, woodMail.ReplyTo));
        Assert.Equal((voltKey.Id, $"\"{volt.Store.Name}\" <obchod@{volt.Domain}>", (string?)null), (voltMail.Key, voltMail.From, voltMail.ReplyTo));
        Assert.Equal((wood.Store.StoreId.ToString(), volt.Store.StoreId.ToString()), (woodMail.Store, voltMail.Store));
        Assert.NotEqual(woodKey.Secret, voltKey.Secret);
        Assert.True(woodDone.Steps[2].KeepsSecret);
        Assert.Equal(woodKey.Secret, await host.Secrets.FindAsync($"provisioning-{woodOperation:N}-2", CancellationToken));
    }

    [Fact]
    public async Task A_store_with_no_domain_of_its_own_sends_through_the_platform_s()
    {
        await using var host = await MailHost.StartAsync(factory);
        var (plain, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(host.Host.Services);

        await host.SendAsync(plain, "buyer@example.test");
        await host.SendAsync(store: null, "colleague@example.test");

        Assert.All(host.Mailgun.Sent, sent => Assert.Equal((MailHost.PlatformDomain, "account"), (sent.Domain, sent.Key)));
        Assert.Equal($"\"{plain.Name}\" <{MailHost.PlatformSender}>", host.Mailgun.Sent.Single(sent => sent.To == "buyer@example.test").From);
        Assert.Equal(2, host.Mailgun.Sent.Count);
    }

    [Fact]
    public async Task A_bounce_for_one_store_does_not_stop_another_writing_to_the_address()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var volt = await host.StoreWithProvedDomainAsync(factory);
        await host.PublishAndFinishAsync(wood, await host.ConnectAndWaitAsync(wood));
        await host.PublishAndFinishAsync(volt, await host.ConnectAndWaitAsync(volt));
        var recipient = $"gone-{Guid.NewGuid():N}@example.test";

        using var bounced = await host.BounceAsync(volt.Store.StoreId, recipient);
        await host.SendAsync(wood.Store, recipient);
        await host.SendAsync(volt.Store, recipient);

        Assert.Equal(HttpStatusCode.OK, bounced.StatusCode);
        Assert.Equal($"mail.{wood.Domain}", Assert.Single(host.Mailgun.Sent, sent => sent.To == recipient).Domain);
    }

    // What the merchant is asked to publish is what Mailgun asked for, field for field, and the platform's own advice
    // is listed apart from it.
    [Fact]
    public async Task The_records_a_store_is_asked_to_publish_are_exactly_the_ones_Mailgun_returned()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        await host.ConnectAndWaitAsync(wood);

        await host.SendAsync(wood.Store, "early@example.test");
        var view = await MailHost.ViewAsync(wood);
        var domain = view.GetProperty("domain");
        var mailgun = Assert.Single(host.Mailgun.Domains);
        var expected = mailgun.Sending().Select(record => Expected("sending", record!))
            .Concat(mailgun.Receiving().Select(record => Expected("receiving", record!)));
        var shown = domain.GetProperty("records").EnumerateArray().Select(record => (
            record.GetProperty("purpose").GetString(),
            record.GetProperty("type").GetString(),
            Nullable(record, "name"),
            record.GetProperty("value").GetString(),
            Nullable(record, "priority"),
            Nullable(record, "valid")));
        var advice = Assert.Single(domain.GetProperty("recommended").EnumerateArray());

        Assert.False(view.GetProperty("sendsFromOwnDomain").GetBoolean());
        Assert.Equal("awaitingDns", domain.GetProperty("state").GetString());
        Assert.Equal("shopforge", mailgun.Selector);
        Assert.Equal(expected, shown);
        Assert.Equal(($"_dmarc.{wood.Domain}", "v=DMARC1; p=none; adkim=r; aspf=r"), (advice.GetProperty("name").GetString(), advice.GetProperty("value").GetString()));
        Assert.Equal(MailHost.PlatformDomain, Assert.Single(host.Mailgun.Sent).Domain);
    }

    [Theory]
    [InlineData("{proved}", "obchod@{proved}", null, "domain")]
    [InlineData("mail.unproved-{label}.example.test", "obchod@unproved-{label}.example.test", null, "domain")]
    [InlineData("mail.{proved}", "obchod@elsewhere.example.test", null, "senderAddress")]
    [InlineData("mail.{proved}", "not-an-address", null, "senderAddress")]
    [InlineData("mail.{proved}", "obchod@{proved}", "not-an-address", "replyTo")]
    [InlineData("mail.{platform}", "obchod@{platform}", null, "domain")]
    [InlineData("mail.{claimed}", "obchod@{claimed}", null, "domain")]
    public async Task A_domain_to_send_from_sits_under_a_proved_one_and_mail_comes_from_either(string domain, string sender, string? replyTo, string field)
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var label = Guid.NewGuid().ToString("N");

        // A name the platform gave the store is in the platform's zone, and a claim nobody proved is nobody's yet.
        var claimed = $"{TestStores.UniqueLabel()}.example.test";
        using var claim = await wood.Owner.PostAsJsonAsync($"/api/admin/stores/{wood.Store.StoreId}/domains", new { HostName = claimed }, CancellationToken);
        string Fill(string text) => text
            .Replace("{proved}", wood.Domain).Replace("{label}", label).Replace("{platform}", wood.Store.HostName).Replace("{claimed}", claimed);

        using var refused = await MailHost.ConnectAsync(wood, Fill(domain), Fill(sender), replyTo);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(CancellationToken)).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(host.Mailgun.Domains);
    }

    // Mail from a domain of the store's own is Mailgun's, and its key needs a vault to be kept in.
    [Fact]
    public async Task A_deployment_without_Mailgun_or_without_a_vault_has_no_domain_to_connect()
    {
        await using var noVault = await MailHost.StartAsync(factory, withVault: false);
        var wood = await noVault.StoreWithProvedDomainAsync(factory);
        var (logged, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var loggedOwner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, logged.TenantId));
        var loggedDomain = await MailHost.ProveAsync(factory, logged, loggedOwner, $"{TestStores.UniqueLabel()}.example.test");

        using var withoutVault = await MailHost.ConnectAsync(wood, $"mail.{wood.Domain}", $"obchod@{wood.Domain}");
        using var withoutMailgun = await MailHost.ConnectAsync(new ProvedStore(logged, loggedOwner, loggedDomain), $"mail.{loggedDomain}", $"obchod@{loggedDomain}");

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (withoutVault.StatusCode, withoutMailgun.StatusCode));
        Assert.Contains("vault", await withoutVault.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Contains("Mailgun", await withoutMailgun.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_store_sends_from_one_domain_and_a_domain_sends_for_one_store()
    {
        await using var host = await MailHost.StartAsync(factory);
        var label = TestStores.UniqueLabel();
        var wood = await host.StoreWithProvedDomainAsync(factory, $"shop.{label}.example.test");
        var parent = await host.StoreWithProvedDomainAsync(factory, $"{label}.example.test");
        await host.ConnectAndWaitAsync(wood);

        using var second = await MailHost.ConnectAsync(wood, $"news.{wood.Domain}", $"obchod@{wood.Domain}");
        using var taken = await MailHost.ConnectAsync(parent, $"mail.{wood.Domain}", $"obchod@{parent.Domain}");

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (second.StatusCode, taken.StatusCode));
        Assert.Contains("remove it first", await second.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Contains("another store", await taken.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Single(host.Mailgun.Domains);
    }

    // A store runs one operation at a time; connecting while another runs is refused, naming the one that runs.
    [Fact]
    public async Task Connecting_while_another_operation_runs_is_refused_with_that_operation()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        await using var scope = TestStores.CreateScope(host.Host.Services, wood.Store);
        var running = await scope.ServiceProvider.GetRequiredService<IProvisioning>().StartAsync(TestOperation.Kind, null, null, CancellationToken);

        using var refused = await MailHost.ConnectAsync(wood, $"mail.{wood.Domain}", $"obchod@{wood.Domain}");
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(CancellationToken)).RootElement;

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(running.OperationId, problem.GetProperty("operationId").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await MailHost.ViewAsync(wood)).GetProperty("domain").ValueKind);
    }

    [Fact]
    public async Task A_removed_domain_sends_the_store_s_mail_through_the_platform_again_and_leaves_nothing_behind()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var connected = await host.ConnectAndWaitAsync(wood);
        await host.PublishAndFinishAsync(wood, connected);

        var removal = await MailHost.StartedAsync(await wood.Owner.DeleteAsync($"/api/admin/stores/{wood.Store.StoreId}/email/domain", CancellationToken));
        var removed = await host.RunToRestAsync(wood.Store, removal);
        await host.SendAsync(wood.Store, "buyer@example.test");
        var view = await MailHost.ViewAsync(wood);

        Assert.Equal(OperationState.Succeeded, removed.State);
        Assert.Empty(host.Mailgun.Domains);
        Assert.Empty(host.Mailgun.Keys);
        Assert.Null(await host.Secrets.FindAsync($"provisioning-{connected:N}-2", CancellationToken));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("domain").ValueKind);
        Assert.Equal(MailHost.PlatformDomain, Assert.Single(host.Mailgun.Sent).Domain);
    }

    // The switch back comes first: once a removal has started, nothing goes out with a key on its way out.
    [Fact]
    public async Task A_store_being_removed_sends_through_the_platform_from_the_first_step()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        await host.PublishAndFinishAsync(wood, await host.ConnectAndWaitAsync(wood));
        host.Mailgun.FailNextKeyDelete();

        var removal = await MailHost.StartedAsync(await wood.Owner.DeleteAsync($"/api/admin/stores/{wood.Store.StoreId}/email/domain", CancellationToken));
        await host.RunAsync(removal);
        var halfway = await host.OperationAsync(wood.Store, removal);
        await host.SendAsync(wood.Store, "buyer@example.test");

        Assert.Equal((StepStatus.Succeeded, StepStatus.Pending, 1), (halfway.Steps[0].Status, halfway.Steps[1].Status, halfway.Steps[1].Attempts));
        Assert.Single(host.Mailgun.Keys);
        Assert.Equal(MailHost.PlatformDomain, Assert.Single(host.Mailgun.Sent).Domain);
    }

    [Fact]
    public async Task Staff_who_do_not_manage_the_store_read_its_mail_but_do_not_change_it()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        using var support = await TestUsers.LoginAsync(
            host.Host, await TestUsers.CreateAsync(host.Host.Services, wood.Store.TenantId, ShopForge.Access.Domain.TenantRole.Support));
        var asSupport = wood with { Owner = support };

        using var read = await support.GetAsync($"/api/admin/stores/{wood.Store.StoreId}/email", CancellationToken);
        using var connected = await MailHost.ConnectAsync(asSupport, $"mail.{wood.Domain}", $"obchod@{wood.Domain}");
        using var removed = await support.DeleteAsync($"/api/admin/stores/{wood.Store.StoreId}/email/domain", CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (read.StatusCode, connected.StatusCode, removed.StatusCode));
        Assert.Empty(host.Mailgun.Domains);
    }

    // Cancelled while it waits for DNS, the operation takes away the key and the domain it made; a domain that was in the
    // account before was not its to take.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_connection_cancelled_while_it_waits_takes_away_only_what_it_made(bool domainWasThere)
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);

        if (domainWasThere)
        {
            host.Mailgun.AddExisting($"mail.{wood.Domain}");
        }

        var operationId = await host.ConnectAndWaitAsync(wood);
        using var cancelled = await wood.Owner.PostAsync($"/api/admin/stores/{wood.Store.StoreId}/operations/{operationId}/cancel", content: null, CancellationToken);
        var operation = await host.RunToRestAsync(wood.Store, operationId);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Empty(host.Mailgun.Keys);
        Assert.Equal(domainWasThere, host.Mailgun.Domains.Count == 1);
        Assert.Null(await host.Secrets.FindAsync($"provisioning-{operationId:N}-2", CancellationToken));
        Assert.Equal(JsonValueKind.Null, (await MailHost.ViewAsync(wood)).GetProperty("domain").ValueKind);
    }

    // A domain already in another Mailgun account is Mailgun's refusal to give again, so a person is told at once, and
    // cancelling takes the store's half-made connection away.
    [Fact]
    public async Task A_domain_Mailgun_has_for_somebody_else_stops_the_connection_with_Mailgun_s_reason()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        host.Mailgun.TakenElsewhere($"mail.{wood.Domain}");

        var operationId = await host.ConnectAndWaitAsync(wood);
        var stopped = await host.OperationAsync(wood.Store, operationId);
        using var cancelled = await wood.Owner.PostAsync($"/api/admin/stores/{wood.Store.StoreId}/operations/{operationId}/cancel", content: null, CancellationToken);
        var operation = await host.RunToRestAsync(wood.Store, operationId);

        Assert.Equal((OperationState.Failed, StepStatus.Failed, 1), (stopped.State, stopped.Steps[1].Status, stopped.Steps[1].Attempts));
        Assert.Contains("already taken", stopped.Steps[1].LastError, StringComparison.Ordinal);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Empty(host.Mailgun.Domains);
        Assert.Equal(JsonValueKind.Null, (await MailHost.ViewAsync(wood)).GetProperty("domain").ValueKind);
    }

    // Mailgun made the key and the answer was lost: the key it finds without its secret is revoked, and another made.
    [Fact]
    public async Task A_key_whose_answer_was_lost_is_revoked_and_made_again()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        host.Mailgun.LoseNextKeyAnswer();

        var operationId = await host.ConnectAndWaitAsync(wood);
        var afterTheLoss = await host.OperationAsync(wood.Store, operationId);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunAsync(operationId);
        var key = Assert.Single(host.Mailgun.Keys);

        Assert.Equal((StepStatus.Pending, 1), (afterTheLoss.Steps[2].Status, afterTheLoss.Steps[2].Attempts));
        Assert.Equal(key.Secret, await host.Secrets.FindAsync($"provisioning-{operationId:N}-2", CancellationToken));
        Assert.Equal(key.Id, (await host.OperationAsync(wood.Store, operationId)).Steps[2].ResourceId);
    }

    // A customer still gets their receipt; the log says why it came from the platform.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_whose_key_is_missing_from_the_vault_sends_through_the_platform(bool leftEmpty)
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var operationId = await host.ConnectAndWaitAsync(wood);
        await host.PublishAndFinishAsync(wood, operationId);
        host.Secrets.Clear();

        if (leftEmpty)
        {
            await host.Secrets.SetAsync($"provisioning-{operationId:N}-2", string.Empty, CancellationToken);
        }

        await host.SendAsync(wood.Store, "buyer@example.test");

        Assert.Equal((MailHost.PlatformDomain, "account"), (Assert.Single(host.Mailgun.Sent).Domain, host.Mailgun.Sent.Single().Key));
    }

    // Waiting for DNS uses up no tries, but not for ever: after a week a person is asked, and a retry picks it up again.
    [Fact]
    public async Task Waiting_for_the_records_stops_for_a_person_after_a_week_and_a_retry_finishes_it()
    {
        await using var host = await MailHost.StartAsync(factory);
        var wood = await host.StoreWithProvedDomainAsync(factory);
        var operationId = await host.ConnectAndWaitAsync(wood);

        host.Clock.Advance(TimeSpan.FromHours(1));
        await host.RunAsync(operationId);
        var anHourOn = await host.OperationAsync(wood.Store, operationId);

        // A week on, the owner's sign-in has long run out, so the retry and the reading are the services' own.
        host.Clock.Advance(TimeSpan.FromDays(8));
        await host.RunAsync(operationId);
        var stopped = await host.OperationAsync(wood.Store, operationId);
        host.Mailgun.Publish($"mail.{wood.Domain}");
        await using var scope = TestStores.CreateScope(host.Host.Services, wood.Store);
        var retried = await scope.ServiceProvider.GetRequiredService<OperationAdmin>().RetryAsync(operationId, CancellationToken);
        var operation = await host.RunToRestAsync(wood.Store, operationId);
        var view = await scope.ServiceProvider.GetRequiredService<Infrastructure.Email.SendingDomainAdmin>().ViewAsync(CancellationToken);

        Assert.Equal((OperationState.Running, StepStatus.Waiting, 2, 0), (anHourOn.State, anHourOn.Steps[3].Status, anHourOn.Steps[3].Checks, anHourOn.Steps[3].Attempts));
        Assert.Equal(OperationState.Failed, stopped.State);
        Assert.Contains("seven days", stopped.Steps[3].LastError, StringComparison.Ordinal);
        Assert.Equal(OperationState.Running, retried.Operation?.State);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.True(view.SendsFromOwnDomain);
    }

    private static (string?, string?, string?, string?, string?, string?) Expected(string purpose, System.Text.Json.Nodes.JsonNode record) => (
        purpose,
        (string?)record["record_type"],
        (string?)record["name"],
        (string?)record["value"],
        (string?)record["priority"],
        (string?)record["valid"]);

    private static string? Nullable(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
