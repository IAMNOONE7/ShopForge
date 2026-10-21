using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Edge;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.IntegrationTests.Email;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Edge;

namespace ShopForge.IntegrationTests.Edge;

// A store's proved domains bound at the edge and taken off it again, and the zones the platform runs for them, as
// operations against a recorded Front Door and a recorded Cloudflare (D-212).
public sealed class EdgeDomainTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The merchant runs their own DNS: they are shown the edge's token, publish it, and the domain is bound in one flow,
    // the route and the security policy every merchant's domain shares made with the first one.
    [Fact]
    public async Task A_domain_proved_at_the_merchant_s_own_dns_host_is_bound_once_the_merchant_publishes_the_edge_s_token()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var www = MerchantHost();
        var operationId = await host.ProveAsync(factory, owned, www);

        await host.RunAsync(operationId);
        var waiting = await EdgeHost.DomainAsync(owned, www);
        var routedBeforeProof = host.FrontDoor.HasRoute("merchants");
        var token = Instruction(waiting, "validation");
        factory.Dns.Publish(token.GetProperty("name").GetString()!, token.GetProperty("value").GetString()!);
        var operation = await host.RunToRestAsync(owned.Store, operationId, TimeSpan.FromMinutes(10));
        var bound = await EdgeHost.DomainAsync(owned, www);
        var domain = host.FrontDoor.DomainFor(www)!;
        var traffic = Assert.Single(bound.GetProperty("edge").GetProperty("records").EnumerateArray());

        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal("binding", waiting.GetProperty("edge").GetProperty("state").GetString());
        Assert.False(routedBeforeProof);
        Assert.Equal(($"_dnsauth.{www}", domain.Token), (token.GetProperty("name").GetString(), token.GetProperty("value").GetString()));
        Assert.Equal("bound", bound.GetProperty("edge").GetProperty("state").GetString());
        Assert.False(bound.GetProperty("edge").GetProperty("recordsByPlatform").GetBoolean());
        Assert.Equal(
            ("CNAME", www, RecordedFrontDoor.EndpointHost, "traffic"),
            (traffic.GetProperty("type").GetString(), traffic.GetProperty("name").GetString(), traffic.GetProperty("value").GetString(), traffic.GetProperty("purpose").GetString()));
        Assert.True(domain.CertificateIssued);
        Assert.StartsWith("m-www-", domain.Name, StringComparison.Ordinal);
        Assert.Equal([domain.Name], host.FrontDoor.RouteDomains("merchants"));
        Assert.Equal([domain.Name], host.FrontDoor.PolicyDomains("merchants"));
        Assert.Equal("Disabled", host.FrontDoor.Route("merchants")!["properties"]!["linkToDefaultDomain"]!.GetValue<string>());
    }

    // The platform runs the domain's DNS: the zone is made and its nameservers shown, the claim is proved by the platform's
    // own challenge record once the nameservers point there, and the binding publishes the edge's token and, last, the
    // traffic record, all without the merchant publishing anything.
    [Fact]
    public async Task A_domain_in_a_zone_the_platform_runs_is_proved_and_bound_without_the_merchant_publishing_anything()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var claim = await EdgeHost.ClaimAsync(owned, www);

        var zoneOperation = await AskForZoneAsync(owned, claim);
        await host.RunAsync(zoneOperation);
        host.Clock.Advance(TimeSpan.FromMinutes(59));
        var early = await host.RunAsync(zoneOperation);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await host.RunAsync(zoneOperation);
        var checks = host.Cloudflare.ActivationChecks.Count;
        var waiting = await EdgeHost.DomainAsync(owned, www);
        host.Cloudflare.Delegate(apex);
        host.Cloudflare.Hide(apex);
        host.Clock.Advance(TimeSpan.FromHours(1));
        await host.RunAsync(zoneOperation);
        var unseen = await EdgeHost.DomainAsync(owned, www);
        host.Cloudflare.Show(apex);
        var zone = await host.RunToRestAsync(owned.Store, zoneOperation);
        var started = await host.BindingsPassAsync(owned.Store);
        var proved = await EdgeHost.DomainAsync(owned, www);
        var binding = await host.RunToRestAsync(owned.Store, proved.GetProperty("edge").GetProperty("operationId").GetGuid(), TimeSpan.FromMinutes(30));
        var bound = await EdgeHost.DomainAsync(owned, www);
        var records = host.Cloudflare.ZoneNamed(apex).Records.Values.ToList();

        Assert.Equal((false, 2), (early, checks));
        Assert.False(unseen.GetProperty("isVerified").GetBoolean());
        Assert.Equal(OperationState.Succeeded, zone.State);
        Assert.Equal((apex, "waiting"), (waiting.GetProperty("zone").GetProperty("name").GetString(), waiting.GetProperty("zone").GetProperty("state").GetString()));
        Assert.Equal(RecordedCloudflare.NameServers, waiting.GetProperty("zone").GetProperty("nameServers").EnumerateArray().Select(server => server.GetString()));
        Assert.Equal(1, started);
        Assert.True(proved.GetProperty("isVerified").GetBoolean());
        Assert.Equal(OperationState.Succeeded, binding.State);
        Assert.Equal("bound", bound.GetProperty("edge").GetProperty("state").GetString());
        Assert.True(bound.GetProperty("edge").GetProperty("recordsByPlatform").GetBoolean());
        Assert.Empty(bound.GetProperty("edge").GetProperty("records").EnumerateArray());
        Assert.Equal("active", bound.GetProperty("zone").GetProperty("state").GetString());
        Assert.Equal($"\"{claim.GetProperty("challengeValue").GetString()}\"", Assert.Single(records, record => record.Name == $"_shopforge-challenge.{www}").Content);
        Assert.Equal($"\"{host.FrontDoor.DomainFor(www)!.Token}\"", Assert.Single(records, record => record.Name == $"_dnsauth.{www}").Content);
        Assert.Equal(RecordedFrontDoor.EndpointHost, Assert.Single(records, record => record.Name == www).Content);
        Assert.All(records, record => Assert.StartsWith("ShopForge, operation ", record.Comment, StringComparison.Ordinal));
        Assert.Equal($"POST CNAME {www}", host.Cloudflare.Writes.Last());
        Assert.True(host.Cloudflare.Timeline.Single(entry => entry.Write == $"POST CNAME {www}").At >= host.FrontDoor.DomainFor(www)!.IssuedAt);
    }

    // A zone already in the platform's account, from an earlier try, is found and used rather than made again, and
    // cancelling leaves it, because this operation did not make it.
    [Fact]
    public async Task A_zone_already_in_the_platform_s_account_is_found_not_made_and_cancelling_leaves_it()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var existing = host.Cloudflare.AddZone(apex, active: false);
        var zoneOperation = await AskForZoneAsync(owned, await EdgeHost.ClaimAsync(owned, www));

        await host.RunAsync(zoneOperation);
        var waiting = await host.OperationAsync(owned.Store, zoneOperation);
        await host.CancelAsync(owned.Store, zoneOperation);
        var cancelled = await host.RunToRestAsync(owned.Store, zoneOperation);
        var zoneStep = waiting.Steps.Single(step => step.Name == "zone");

        Assert.Equal((existing.Id, false), (zoneStep.ResourceId, zoneStep.Created));
        Assert.Equal(OperationState.Cancelled, cancelled.State);
        Assert.Same(existing, host.Cloudflare.ZoneNamed(apex));
        Assert.Equal(JsonValueKind.Null, (await EdgeHost.DomainAsync(owned, www)).GetProperty("zone").ValueKind);
    }

    // Records already right are left alone: a zone that already sends the domain's traffic to the endpoint is not written
    // to for it again, while the edge's new token, which it did not have, is.
    [Fact]
    public async Task A_record_that_already_has_the_right_value_is_not_written_again()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var zone = host.Cloudflare.AddZone(apex, active: true);
        zone.Records["traffic"] = new FakeRecord("traffic", "CNAME", www, RecordedFrontDoor.EndpointHost, 1, false, "set up earlier");
        zone.Records["stray"] = new FakeRecord("stray", "CNAME", www, "old-site.example.test", 1, false, "left over");
        var zoneOperation = await AskForZoneAsync(owned, await EdgeHost.ClaimAsync(owned, www));

        Assert.Equal(OperationState.Succeeded, (await host.RunToRestAsync(owned.Store, zoneOperation)).State);
        await host.BindingsPassAsync(owned.Store);
        var bindOperation = (await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("operationId").GetGuid();
        var binding = await host.RunToRestAsync(owned.Store, bindOperation, TimeSpan.FromMinutes(30));

        Assert.Equal(OperationState.Succeeded, binding.State);
        Assert.DoesNotContain(host.Cloudflare.Writes, write => write == $"POST CNAME {www}" || write == $"PUT CNAME {www}");
        Assert.Contains($"DELETE CNAME {www}", host.Cloudflare.Writes);
        Assert.Contains($"POST TXT _dnsauth.{www}", host.Cloudflare.Writes);
        Assert.Equal("traffic", Assert.Single(zone.RecordsNamed(www, "CNAME")).Id);
    }

    // A provider that asks to be left alone is waited out for as long as it said, using up no tries: Resource Manager's
    // Retry-After, Cloudflare's, Cloudflare's RateLimit header when that is all it sends, and the five minutes Cloudflare
    // blocks for when it says nothing at all. A step asked again finds what it made before the 429 rather than making it
    // twice.
    [Theory]
    [InlineData("azure", "retry-after", 120)]
    [InlineData("cloudflare", "retry-after", 120)]
    [InlineData("cloudflare", "ratelimit", 90)]
    [InlineData("cloudflare", "none", 300)]
    public async Task A_429_is_waited_out_for_as_long_as_the_provider_said(string provider, string header, int seconds)
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var operationId = await host.ProveAsync(factory, owned, MerchantHost());

        if (provider == "azure")
        {
            host.FrontDoor.ThrottleNext(seconds);
        }
        else
        {
            host.Cloudflare.ThrottleNext(seconds, header);
        }

        var throttledAt = host.Clock.GetUtcNow();
        await host.RunAsync(operationId);
        var throttled = await host.OperationAsync(owned.Store, operationId);
        host.Clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        var early = await host.RunAsync(operationId);
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        var onTime = await host.RunAsync(operationId);
        var afterwards = await host.OperationAsync(owned.Store, operationId);
        var step = throttled.Steps.Single(candidate => candidate.Name == "edge-domain");

        Assert.Equal((StepStatus.Waiting, 0), (step.Status, step.Attempts));
        Assert.InRange(throttled.NextCheckAt!.Value, throttledAt.AddSeconds(seconds).AddMilliseconds(-1), throttledAt.AddSeconds(seconds).AddMilliseconds(1));
        Assert.False(early);
        Assert.True(onTime);
        Assert.Equal(StepStatus.Succeeded, afterwards.Steps.Single(candidate => candidate.Name == "edge-domain").Status);
        Assert.Single(host.FrontDoor.Domains);
    }

    // A certificate still pending an hour after its domain is approved and routed is waited for, using up no tries, and
    // the domain is bound once it is issued.
    [Fact]
    public async Task A_certificate_pending_for_an_hour_is_waited_for_and_the_domain_bound_once_it_is_issued()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        host.FrontDoor.CertificateTakes = TimeSpan.FromHours(1);
        var owned = await host.StoreAsync();
        var www = MerchantHost();
        var operationId = await host.ProveAsync(factory, owned, www);
        await host.RunAsync(operationId);
        var token = Instruction(await EdgeHost.DomainAsync(owned, www), "validation");
        factory.Dns.Publish(token.GetProperty("name").GetString()!, token.GetProperty("value").GetString()!);
        host.Clock.Advance(TimeSpan.FromMinutes(2));

        await host.RunAsync(operationId);
        var routedAt = host.Clock.GetUtcNow();
        var states = new List<string?>();

        while (host.Clock.GetUtcNow() - routedAt < TimeSpan.FromMinutes(55))
        {
            host.Clock.Advance(TimeSpan.FromMinutes(5));
            await host.RunAsync(operationId);
            states.Add((await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("state").GetString());
        }

        var operation = await host.RunToRestAsync(owned.Store, operationId, TimeSpan.FromMinutes(5));
        var certificate = operation.Steps.Single(step => step.Name == "certificate");

        Assert.All(states, state => Assert.Equal("binding", state));
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(0, certificate.Attempts);
        Assert.True(certificate.Checks > 1);
        Assert.True(host.FrontDoor.DomainFor(www)!.CertificateIssued);
        Assert.Equal("bound", (await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("state").GetString());
    }

    // Forty-five days before a renewal Front Door cannot do by itself, it wants the domain proved again. The pass sees it,
    // and the revalidation asks for a new token and republishes it where Front Door reads it, which approves the domain
    // again without the merchant.
    [Fact]
    public async Task A_revalidation_Front_Door_asks_for_republishes_its_new_token_in_the_zone_the_platform_runs()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var (owned, apex, www) = await BoundInPlatformZoneAsync(host);
        var oldToken = host.FrontDoor.DomainFor(www)!.Token;
        var whileApproved = await host.ValidationsPassAsync();
        host.FrontDoor.RequireRevalidation(www);

        var started = await host.ValidationsPassAsync();
        var revalidation = (await host.OperationsOnAsync(owned.Store, www)).First(operation => operation.Kind == EdgeOperations.Revalidate);
        var operation = await host.RunToRestAsync(owned.Store, revalidation.Id, TimeSpan.FromMinutes(5));
        var domain = host.FrontDoor.DomainFor(www)!;

        Assert.Equal((0, 1), (whileApproved, started));
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal("Approved", domain.Validation);
        Assert.NotEqual(oldToken, domain.Token);
        Assert.Equal($"\"{domain.Token}\"", Assert.Single(host.Cloudflare.ZoneNamed(apex).RecordsNamed($"_dnsauth.{www}", "TXT")).Content);
        Assert.Equal("bound", (await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("state").GetString());
    }

    // Two stores' domains bound at once both end up on the route and the policy every merchant's domain shares. Neither
    // carries an ETag, and a slow read lets two writers cross, so the runtime writes them one at a time under its lock:
    // one write makes the route, the next adds to it, and neither loses the other's domain.
    [Fact]
    public async Task Two_domains_bound_at_once_both_end_up_on_the_shared_route_and_policy()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var stores = new[] { await host.StoreAsync(), await host.StoreAsync() };
        var hosts = new[] { MerchantHost(), MerchantHost() };
        var operations = new[] { await host.ProveAsync(factory, stores[0], hosts[0]), await host.ProveAsync(factory, stores[1], hosts[1]) };
        await Task.WhenAll(operations.Select(host.RunAsync));

        for (var index = 0; index < 2; index++)
        {
            var token = Instruction(await EdgeHost.DomainAsync(stores[index], hosts[index]), "validation");
            factory.Dns.Publish(token.GetProperty("name").GetString()!, token.GetProperty("value").GetString()!);
        }

        host.FrontDoor.RouteReadTakes = TimeSpan.FromMilliseconds(500);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await Task.WhenAll(operations.Select(host.RunAsync));
        var names = hosts.Select(hostName => host.FrontDoor.DomainFor(hostName)!.Name).Order().ToList();

        Assert.Equal(names, host.FrontDoor.RouteDomains("merchants").Order());
        Assert.Equal(names, host.FrontDoor.PolicyDomains("merchants").Order());
        Assert.Equal(2, host.FrontDoor.Writes.Count(write => write == "PUT route merchants"));
    }

    // Removing a bound domain takes it off the edge: the store stops answering on it at once, and then the traffic record,
    // the route and the policy (deleted with their last domain), the custom domain and its validation record go, and the
    // binding is forgotten.
    [Fact]
    public async Task Removing_a_bound_domain_takes_everything_off_the_edge_and_out_of_the_zone()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var (owned, apex, www) = await BoundInPlatformZoneAsync(host);
        var domainId = (await EdgeHost.DomainAsync(owned, www)).GetProperty("id").GetGuid();
        var writesBefore = host.Cloudflare.Writes.Count;

        using var removed = await owned.Owner.DeleteAsync($"/api/admin/stores/{owned.Store.StoreId}/domains/{domainId}", CancellationToken);
        var unbinding = (await host.OperationsOnAsync(owned.Store, www)).First(operation => operation.Kind == EdgeOperations.Unbind);
        var alongside = await host.BindingsPassAsync(owned.Store);
        var operation = await host.RunToRestAsync(owned.Store, unbinding.Id, TimeSpan.FromMinutes(5));
        var zone = host.Cloudflare.ZoneNamed(apex);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, alongside);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Null(host.FrontDoor.DomainFor(www));
        Assert.False(host.FrontDoor.HasRoute("merchants"));
        Assert.False(host.FrontDoor.HasPolicy("merchants"));
        Assert.Empty(zone.RecordsNamed(www, "CNAME"));
        Assert.Empty(zone.RecordsNamed($"_dnsauth.{www}", "TXT"));
        Assert.Equal($"DELETE CNAME {www}", host.Cloudflare.Writes.Skip(writesBefore).First());
        Assert.False(await factory.QueryAsync(owned.Store, dbContext => dbContext.Set<DomainBinding>().AnyAsync(binding => binding.HostName == www, CancellationToken)));
    }

    // A domain removed while it is still being bound has its binding cancelled, which takes away what the binding made;
    // nothing is left for an unbinding to do.
    [Fact]
    public async Task Removing_a_domain_while_it_is_being_bound_cancels_the_binding_and_leaves_nothing_at_the_edge()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var www = MerchantHost();
        var operationId = await host.ProveAsync(factory, owned, www);
        await host.RunAsync(operationId);
        var domainId = (await EdgeHost.DomainAsync(owned, www)).GetProperty("id").GetGuid();

        using var removed = await owned.Owner.DeleteAsync($"/api/admin/stores/{owned.Store.StoreId}/domains/{domainId}", CancellationToken);
        var operation = await host.RunToRestAsync(owned.Store, operationId);
        var leftOver = await host.BindingsPassAsync(owned.Store);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Null(host.FrontDoor.DomainFor(www));
        Assert.Equal(0, leftOver);
        Assert.False(await factory.QueryAsync(owned.Store, dbContext => dbContext.Set<DomainBinding>().AnyAsync(binding => binding.HostName == www, CancellationToken)));
    }

    // Cancelled, a binding takes away the custom domain it made and leaves one Front Door already had, as from an earlier
    // binding of the same host whose clean-up never finished. Two domains of one store are bound side by side (D-212).
    [Fact]
    public async Task Cancelling_a_binding_takes_away_the_domain_it_made_and_leaves_one_it_found()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (made, found) = (MerchantHost(), MerchantHost());
        host.FrontDoor.AddPlatformDomain(FrontDoorDomains.ResourceNameFor(found), found);
        var operations = new[] { await host.ProveAsync(factory, owned, made), await host.ProveAsync(factory, owned, found) };
        var outcomes = new List<OperationDetail>();

        foreach (var operationId in operations)
        {
            await host.RunAsync(operationId);
            await host.CancelAsync(owned.Store, operationId);
            outcomes.Add(await host.RunToRestAsync(owned.Store, operationId));
        }

        Assert.All(outcomes, outcome => Assert.Equal(OperationState.Cancelled, outcome.State));
        Assert.Null(host.FrontDoor.DomainFor(made));
        Assert.NotNull(host.FrontDoor.DomainFor(found));
        Assert.False(outcomes[1].Steps.Single(step => step.Name == "edge-domain").Created);
    }

    // Cancelled while it waits for its certificate, a binding in a zone the platform runs takes away everything it made:
    // the validation record it published, its place on the route and the policy, and the custom domain.
    [Fact]
    public async Task Cancelling_a_binding_in_a_zone_the_platform_runs_takes_away_the_records_it_published()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        host.FrontDoor.CertificateTakes = TimeSpan.FromHours(6);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var zoneOperation = await AskForZoneAsync(owned, await EdgeHost.ClaimAsync(owned, www));
        await host.RunAsync(zoneOperation);
        host.Cloudflare.Delegate(apex);
        Assert.Equal(OperationState.Succeeded, (await host.RunToRestAsync(owned.Store, zoneOperation)).State);
        await host.BindingsPassAsync(owned.Store);
        var bindOperation = (await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("operationId").GetGuid();

        await host.RunAsync(bindOperation);
        var published = host.Cloudflare.ZoneNamed(apex).RecordsNamed($"_dnsauth.{www}", "TXT").Count;
        var routed = host.FrontDoor.HasRoute("merchants");
        await host.CancelAsync(owned.Store, bindOperation);
        var operation = await host.RunToRestAsync(owned.Store, bindOperation);
        var zone = host.Cloudflare.ZoneNamed(apex);

        Assert.Equal((1, true), (published, routed));
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Empty(zone.RecordsNamed($"_dnsauth.{www}", "TXT"));
        Assert.Empty(zone.RecordsNamed(www, "CNAME"));
        Assert.Null(host.FrontDoor.DomainFor(www));
        Assert.False(host.FrontDoor.HasRoute("merchants"));
        Assert.False(host.FrontDoor.HasPolicy("merchants"));
    }

    // A merchant who never publishes the edge's token is not waited for for ever: after fourteen days, a new token on the
    // way among them, the binding stops for a person, naming the record it still needs.
    [Fact]
    public async Task A_binding_whose_token_is_never_published_stops_for_a_person_after_fourteen_days()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var www = MerchantHost();
        var operationId = await host.ProveAsync(factory, owned, www);

        var operation = await host.RunToRestAsync(owned.Store, operationId, TimeSpan.FromHours(12));
        var validation = operation.Steps.Single(step => step.Name == "validation");

        Assert.Equal(OperationState.Failed, operation.State);
        Assert.Equal((StepStatus.Failed, 1), (validation.Status, validation.Attempts));
        Assert.Contains("in fourteen days", validation.LastError, StringComparison.Ordinal);
        Assert.Contains($"_dnsauth.{www}", validation.LastError, StringComparison.Ordinal);
    }

    // Cancelling a zone deletes the zone it made only while no nameserver points at it: once the domain answers from it,
    // deleting it would take the merchant's domain off the internet.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_a_zone_deletes_it_only_while_nothing_points_at_it(bool delegated)
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var zoneOperation = await AskForZoneAsync(owned, await EdgeHost.ClaimAsync(owned, www));
        await host.RunAsync(zoneOperation);

        if (delegated)
        {
            host.Cloudflare.Delegate(apex);
        }

        await host.CancelAsync(owned.Store, zoneOperation);
        var operation = await host.RunToRestAsync(owned.Store, zoneOperation);

        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Equal(delegated, host.Cloudflare.HasZone(apex));
    }

    // A domain removed while Front Door's revalidation of it is under way: the revalidation is asked to stop, and the pass
    // unbinds the domain once nothing else has it.
    [Fact]
    public async Task A_domain_removed_during_its_revalidation_is_unbound_by_the_pass_once_that_has_stopped()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var (owned, _, www) = await BoundInPlatformZoneAsync(host);
        host.FrontDoor.RequireRevalidation(www);
        await host.ValidationsPassAsync();
        var revalidation = (await host.OperationsOnAsync(owned.Store, www)).First(operation => operation.Kind == EdgeOperations.Revalidate);
        var domainId = (await EdgeHost.DomainAsync(owned, www)).GetProperty("id").GetGuid();

        using var removed = await owned.Owner.DeleteAsync($"/api/admin/stores/{owned.Store.StoreId}/domains/{domainId}", CancellationToken);
        var stopped = await host.RunToRestAsync(owned.Store, revalidation.Id);
        var started = await host.BindingsPassAsync(owned.Store);
        var unbinding = (await host.OperationsOnAsync(owned.Store, www)).First(operation => operation.Kind == EdgeOperations.Unbind);
        var operation = await host.RunToRestAsync(owned.Store, unbinding.Id, TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(OperationState.Cancelled, stopped.State);
        Assert.Equal(1, started);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Null(host.FrontDoor.DomainFor(www));
    }

    // A role granted moments ago takes minutes to reach Resource Manager, so a refusal of the app's permission is tried
    // again rather than taken as final.
    [Fact]
    public async Task A_refusal_of_the_app_s_permission_is_tried_again()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var operationId = await host.ProveAsync(factory, owned, MerchantHost());
        host.FrontDoor.ForbidNext();

        await host.RunAsync(operationId);
        var operation = await host.OperationAsync(owned.Store, operationId);
        var step = operation.Steps.Single(candidate => candidate.Name == "edge-domain");

        Assert.Equal(OperationState.Running, operation.State);
        Assert.Equal((StepStatus.Pending, 1), (step.Status, step.Attempts));
        Assert.Contains("Azure answered 403", step.LastError, StringComparison.Ordinal);
    }

    // A zone the platform made whose nameservers do not point at it yet answers for nobody, so a domain under it proved by
    // hand keeps its records the merchant's to publish.
    [Fact]
    public async Task A_zone_still_waiting_for_its_nameservers_does_not_take_the_domain_s_records()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var claim = await EdgeHost.ClaimAsync(owned, www);
        await host.RunAsync(await AskForZoneAsync(owned, claim));
        factory.Dns.Publish(claim.GetProperty("challengeName").GetString()!, claim.GetProperty("challengeValue").GetString()!);

        using var verified = await owned.Owner.PostAsync(
            $"/api/admin/stores/{owned.Store.StoreId}/domains/{claim.GetProperty("id").GetGuid()}/verify", content: null, CancellationToken);
        await host.RunAsync((await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("operationId").GetGuid());
        var shown = await EdgeHost.DomainAsync(owned, www);

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.False(shown.GetProperty("edge").GetProperty("recordsByPlatform").GetBoolean());
        Assert.Equal($"_dnsauth.{www}", Instruction(shown, "validation").GetProperty("name").GetString());
        Assert.Empty(host.Cloudflare.ZoneNamed(apex).Records);
    }

    // A provider that stops answering does not hold a step past its timeout: the adapter gives up with the step's token,
    // the step is put down as a try that failed, and it is asked again later (D-210).
    [Theory]
    [InlineData("azure")]
    [InlineData("cloudflare")]
    public async Task A_provider_that_stops_answering_lets_the_step_go_at_its_timeout(string provider)
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var operationId = await host.ProveAsync(factory, owned, MerchantHost());

        if (provider == "azure")
        {
            host.FrontDoor.HangNext();
        }
        else
        {
            host.Cloudflare.HangNext();
        }

        var watch = Stopwatch.StartNew();
        await host.RunAsync(operationId);
        watch.Stop();
        var step = (await host.OperationAsync(owned.Store, operationId)).Steps.Single(candidate => candidate.Name == "edge-domain");

        Assert.InRange(watch.Elapsed, EdgeHost.StepTimeout - TimeSpan.FromSeconds(1), EdgeHost.StepTimeout + TimeSpan.FromSeconds(10));
        Assert.Equal((StepStatus.Pending, 1), (step.Status, step.Attempts));
        Assert.Contains($"No answer within {EdgeHost.StepTimeout.TotalSeconds:0} seconds", step.LastError, StringComparison.Ordinal);
    }

    // Running DNS is for a claim not yet proved, under a zone that holds it and is nobody else's, and only where the
    // deployment runs DNS for merchants at all.
    [Fact]
    public async Task Asking_the_platform_to_run_a_zone_is_refused_where_it_cannot_or_must_not()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var other = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var claim = await EdgeHost.ClaimAsync(owned, www);
        var proved = MerchantHost();
        await host.ProveAsync(factory, owned, proved);
        await AskForZoneAsync(other, await EdgeHost.ClaimAsync(other, $"shop.{apex}"), apex);

        using var taken = await AskAsync(owned, claim.GetProperty("id").GetGuid(), apex);
        using var outside = await AskAsync(owned, claim.GetProperty("id").GetGuid(), "elsewhere.test");
        using var single = await AskAsync(owned, claim.GetProperty("id").GetGuid(), "test");
        using var already = await AskAsync(owned, (await EdgeHost.DomainAsync(owned, proved)).GetProperty("id").GetGuid(), null);

        await using var withoutZones = await EdgeHost.StartAsync(factory, withZones: false);
        var elsewhere = await withoutZones.StoreAsync();
        using var notHere = await AskAsync(elsewhere, (await EdgeHost.ClaimAsync(elsewhere, ZoneHosts().Www)).GetProperty("id").GetGuid(), null);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("already taken", await taken.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Equal(
            (HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest),
            (outside.StatusCode, single.StatusCode, already.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, notHere.StatusCode);
        Assert.Contains("does not run DNS", await notHere.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // A domain proved before this deployment bound domains at the edge is bound by the pass, once.
    [Fact]
    public async Task The_pass_binds_a_proved_domain_that_has_no_binding()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var www = MerchantHost();
        await MailHost.ProveAsync(factory, store, owner, www);
        await using var host = await EdgeHost.StartAsync(factory);

        var started = await host.BindingsPassAsync(store);
        var again = await host.BindingsPassAsync(store);

        Assert.Equal((1, 0), (started, again));
        Assert.Equal(EdgeOperations.Bind, Assert.Single(await host.OperationsOnAsync(store, www)).Kind);
    }

    // The pass looks only at stores with something to do: a proved domain with no binding, or a binding whose domain has
    // gone. A store whose domains are all bound is left alone.
    [Fact]
    public async Task The_pass_finds_the_stores_with_something_to_do_and_no_others()
    {
        var (unbound, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, unbound.TenantId));
        await MailHost.ProveAsync(factory, unbound, owner, MerchantHost());
        await using var host = await EdgeHost.StartAsync(factory);
        var (bound, _, _) = await BoundInPlatformZoneAsync(host);
        var (leaving, _, leavingHost) = await BoundInPlatformZoneAsync(host);
        var leavingId = (await EdgeHost.DomainAsync(leaving, leavingHost)).GetProperty("id").GetGuid();
        using var removed = await leaving.Owner.DeleteAsync($"/api/admin/stores/{leaving.Store.StoreId}/domains/{leavingId}", CancellationToken);
        await using var scope = host.Host.Services.CreateAsyncScope();

        var stores = (await DomainBindingsPass.StoresWithWorkAsync(scope.ServiceProvider, CancellationToken)).Select(store => store.StoreId).ToList();

        Assert.Contains(unbound.StoreId, stores);
        Assert.Contains(leaving.Store.StoreId, stores);
        Assert.DoesNotContain(bound.Store.StoreId, stores);
    }

    // The template's own domains are kept by the pass in a zone the platform runs: their tokens published while Front Door
    // wants them, their traffic records pointed straight at the endpoint, and, once a token has run out, a new one asked
    // for and published.
    [Fact]
    public async Task The_pass_keeps_the_template_s_own_domains_proved_and_pointed_at_the_endpoint()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var platform = $"platform-{Guid.NewGuid():N}.test";
        var zone = host.Cloudflare.AddZone(platform, active: true);
        var stores = host.FrontDoor.AddPlatformDomain("stores", $"*.shops.{platform}");
        var admin = host.FrontDoor.AddPlatformDomain("admin", $"admin.{platform}");

        await host.ValidationsPassAsync();
        var firstToken = stores.Token;
        host.FrontDoor.RequireRevalidation(stores.HostName);
        await host.ValidationsPassAsync();
        await host.ValidationsPassAsync();

        Assert.NotEqual(firstToken, stores.Token);
        Assert.Equal($"\"{stores.Token}\"", Assert.Single(zone.RecordsNamed($"_dnsauth.shops.{platform}", "TXT")).Content);
        Assert.Equal($"\"{admin.Token}\"", Assert.Single(zone.RecordsNamed($"_dnsauth.admin.{platform}", "TXT")).Content);
        Assert.Equal(RecordedFrontDoor.EndpointHost, Assert.Single(zone.RecordsNamed($"*.shops.{platform}", "CNAME")).Content);
        Assert.Equal(RecordedFrontDoor.EndpointHost, Assert.Single(zone.RecordsNamed($"admin.{platform}", "CNAME")).Content);
        Assert.Equal("Approved", admin.Validation);
        Assert.All(zone.Records.Values, record => Assert.Equal(EdgeValidationsPass.PlatformComment, record.Comment));
    }

    // A merchant's domain at the edge that no store binds, left by a clean-up that never finished, is reported for a
    // person and left where it is: deleting what nothing explains is not the pass's to do.
    [Fact]
    public async Task A_merchant_domain_at_the_edge_that_no_store_binds_is_left_for_a_person()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var orphan = MerchantHost();
        host.FrontDoor.AddPlatformDomain(FrontDoorDomains.ResourceNameFor(orphan), orphan);

        var started = await host.ValidationsPassAsync();

        Assert.Equal(0, started);
        Assert.NotNull(host.FrontDoor.DomainFor(orphan));
    }

    // A host Front Door will not take, because another profile has it, stops the binding for a person with Front Door's
    // own words rather than being tried eight times.
    [Fact]
    public async Task A_domain_another_front_door_profile_has_stops_the_binding_for_a_person()
    {
        await using var host = await EdgeHost.StartAsync(factory);
        var owned = await host.StoreAsync();
        var www = MerchantHost();
        host.FrontDoor.TakenElsewhere(www);
        var operationId = await host.ProveAsync(factory, owned, www);

        var operation = await host.RunToRestAsync(owned.Store, operationId);
        var step = operation.Steps.Single(candidate => candidate.Name == "edge-domain");

        Assert.Equal(OperationState.Failed, operation.State);
        Assert.Equal((StepStatus.Failed, 1), (step.Status, step.Attempts));
        Assert.Contains("already in use by another Front Door profile", step.LastError, StringComparison.Ordinal);
    }

    // Some of the edge's names and not all, or a Cloudflare token without its account, is a deployment that would bind
    // or publish nothing without a word, so the application refuses to start.
    [Theory]
    [InlineData("Edge:Domains:Profile", "shopforge-test-fd", "Edge:Domains")]
    [InlineData("Dns:Cloudflare:ApiToken", "cloudflare-token-made-up", "Dns:Cloudflare")]
    public void A_deployment_that_names_only_part_of_the_edge_or_of_cloudflare_refuses_to_start(string setting, string value, string section)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var refused = Assert.ThrowsAny<Exception>(() => misconfigured.Services);

        Assert.Contains(section, refused.ToString(), StringComparison.Ordinal);
    }

    // A store's www domain, proved and bound in a zone the platform runs.
    private static async Task<(OwnedStore Owned, string Apex, string Www)> BoundInPlatformZoneAsync(EdgeHost host)
    {
        var owned = await host.StoreAsync();
        var (apex, www) = ZoneHosts();
        var zoneOperation = await AskForZoneAsync(owned, await EdgeHost.ClaimAsync(owned, www));

        await host.RunAsync(zoneOperation);
        host.Cloudflare.Delegate(apex);
        Assert.Equal(OperationState.Succeeded, (await host.RunToRestAsync(owned.Store, zoneOperation)).State);
        await host.BindingsPassAsync(owned.Store);
        var bindOperation = (await EdgeHost.DomainAsync(owned, www)).GetProperty("edge").GetProperty("operationId").GetGuid();
        Assert.Equal(OperationState.Succeeded, (await host.RunToRestAsync(owned.Store, bindOperation, TimeSpan.FromMinutes(30))).State);

        return (owned, apex, www);
    }

    private static async Task<Guid> AskForZoneAsync(OwnedStore owned, JsonElement claim, string? zone = null)
    {
        using var asked = await AskAsync(owned, claim.GetProperty("id").GetGuid(), zone);
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);

        return JsonDocument.Parse(await asked.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("operationId").GetGuid();
    }

    private static Task<HttpResponseMessage> AskAsync(OwnedStore owned, Guid domainId, string? zone) =>
        owned.Owner.PostAsJsonAsync($"/api/admin/stores/{owned.Store.StoreId}/domains/{domainId}/zone", new { Zone = zone }, CancellationToken);

    private static JsonElement Instruction(JsonElement domain, string purpose) =>
        domain.GetProperty("edge").GetProperty("records").EnumerateArray().Single(record => record.GetProperty("purpose").GetString() == purpose);

    // A merchant's domain at a DNS host of their own.
    private static string MerchantHost() => $"www.{TestStores.UniqueLabel()}.example.test";

    // A domain of its own whose DNS the platform can run: a zone nobody else's test has.
    private static (string Apex, string Www) ZoneHosts()
    {
        var apex = $"shop-{Guid.NewGuid():N}.test";

        return (apex, $"www.{apex}");
    }
}
