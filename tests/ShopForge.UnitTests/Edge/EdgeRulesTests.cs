using System.Text.RegularExpressions;
using ShopForge.Infrastructure.Dns;
using ShopForge.Infrastructure.Edge;
using ShopForge.Shared.Edge;

namespace ShopForge.UnitTests.Edge;

// What the edge's answers mean, what the runtime names its domains, and how TXT values travel (D-212).
public sealed partial class EdgeRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    // A domain is ready once it is proved, its certificate issued and nothing of it still on its way to the edge; a
    // deployment the edge never starts counts as done.
    [Theory]
    [InlineData(EdgeValidation.Approved, true, EdgeProvisioning.Succeeded, EdgeDeployment.Succeeded, true)]
    [InlineData(EdgeValidation.Approved, true, EdgeProvisioning.Succeeded, EdgeDeployment.NotStarted, true)]
    [InlineData(EdgeValidation.Approved, true, EdgeProvisioning.Succeeded, EdgeDeployment.InProgress, false)]
    [InlineData(EdgeValidation.Approved, true, EdgeProvisioning.Updating, EdgeDeployment.Succeeded, false)]
    [InlineData(EdgeValidation.Approved, false, EdgeProvisioning.Succeeded, EdgeDeployment.Succeeded, false)]
    [InlineData(EdgeValidation.Pending, true, EdgeProvisioning.Succeeded, EdgeDeployment.Succeeded, false)]
    [InlineData(EdgeValidation.PendingRevalidation, true, EdgeProvisioning.Succeeded, EdgeDeployment.Succeeded, false)]
    public void A_domain_is_ready_once_proved_issued_and_in_place(
        EdgeValidation validation, bool issued, EdgeProvisioning provisioning, EdgeDeployment deployment, bool ready) =>
        Assert.Equal(ready, Domain(validation, "token", Now.AddDays(1), provisioning, deployment, issued).IsReady);

    // The edge wants a new token published when the old one timed out, was refused or failed it, or ran out while it
    // still waits for one: a revalidation forty-five days before a renewal comes with a token long expired.
    [Theory]
    [InlineData(EdgeValidation.TimedOut, 1, true)]
    [InlineData(EdgeValidation.Rejected, 1, true)]
    [InlineData(EdgeValidation.InternalError, 1, true)]
    [InlineData(EdgeValidation.Pending, -1, true)]
    [InlineData(EdgeValidation.PendingRevalidation, -100, true)]
    [InlineData(EdgeValidation.Pending, 1, false)]
    [InlineData(EdgeValidation.PendingRevalidation, 1, false)]
    [InlineData(EdgeValidation.Approved, -1, false)]
    [InlineData(EdgeValidation.RefreshingValidationToken, -1, false)]
    [InlineData(EdgeValidation.Submitting, -1, false)]
    public void A_new_token_is_wanted_once_the_old_one_cannot_prove_the_domain_any_more(EdgeValidation validation, int expiresInDays, bool wanted) =>
        Assert.Equal(wanted, Domain(validation, "token", Now.AddDays(expiresInDays)).NeedsNewProof(Now));

    [Fact]
    public void A_token_is_live_while_it_is_there_and_not_past_its_date()
    {
        Assert.True(Domain(EdgeValidation.Pending, "token", Now.AddSeconds(1)).HasLiveToken(Now));
        Assert.True(Domain(EdgeValidation.Pending, "token", expires: null).HasLiveToken(Now));
        Assert.False(Domain(EdgeValidation.Pending, "token", Now).HasLiveToken(Now));
        Assert.False(Domain(EdgeValidation.Pending, token: null, Now.AddDays(1)).HasLiveToken(Now));
        Assert.False(Domain(EdgeValidation.Pending, string.Empty, Now.AddDays(1)).HasLiveToken(Now));
    }

    // The proof's name is `_dnsauth.<host>`, and for a wildcard the name it stands under (UNVERIFIED until V19).
    [Theory]
    [InlineData("www.shop.cz", "_dnsauth.www.shop.cz")]
    [InlineData("shop.cz", "_dnsauth.shop.cz")]
    [InlineData("*.shops.example.cz", "_dnsauth.shops.example.cz")]
    public void The_validation_record_is_named_for_the_host(string hostName, string record) =>
        Assert.Equal(record, EdgeNames.ValidationRecord(hostName));

    // A merchant's domain is named for its host and a hash of it: the same on every replica and every retry, readable,
    // within what Resource Manager accepts, and never the name of one of the template's.
    [Theory]
    [InlineData("www.shop.cz")]
    [InlineData("WWW.Shop.CZ")]
    [InlineData("xn--bcher-kva.cz")]
    [InlineData("a-very-long-label-that-goes-on-and-on-and-on.and-then-another-label-as-well.example.cz")]
    public void A_merchant_s_domain_is_named_for_its_host(string hostName)
    {
        var name = FrontDoorDomains.ResourceNameFor(hostName);
        var label = hostName.ToLowerInvariant().Split('.')[0];

        Assert.Equal(name, FrontDoorDomains.ResourceNameFor(hostName.ToLowerInvariant()));
        Assert.Matches(ResourceName(), name);
        Assert.True(name.Length <= 64);
        Assert.True(EdgeNames.IsMerchants(name));
        Assert.StartsWith($"m-{label[..Math.Min(10, label.Length)]}", name, StringComparison.Ordinal);
    }

    // Two hosts that read the same once their dots become dashes are still two domains.
    [Fact]
    public void Hosts_that_read_alike_are_still_named_apart()
    {
        Assert.NotEqual(FrontDoorDomains.ResourceNameFor("www-shop.cz"), FrontDoorDomains.ResourceNameFor("www.shop-cz"));
        Assert.False(EdgeNames.IsMerchants("admin"));
        Assert.False(EdgeNames.IsMerchants("stores"));
    }

    [Theory]
    [InlineData("token-1", "\"token-1\"")]
    [InlineData("v=spf1 include:mailgun.org ~all", "\"v=spf1 include:mailgun.org ~all\"")]
    [InlineData("say \"hi\" \\ there", "\"say \\\"hi\\\" \\\\ there\"")]
    public void A_TXT_value_travels_as_a_quoted_string_and_comes_back_as_it_was(string value, string quoted)
    {
        Assert.Equal(quoted, CloudflareZones.Quote(value));
        Assert.Equal(value, CloudflareZones.Unquote(quoted));
    }

    // A long value is split into several quoted strings, which a resolver joins; content never quoted is taken as it is.
    [Theory]
    [InlineData("\"abc\" \"def\"", "abcdef")]
    [InlineData("  \"abc\"  ", "abc")]
    [InlineData("plain", "plain")]
    public void A_TXT_record_s_strings_are_joined_as_a_resolver_joins_them(string content, string value) =>
        Assert.Equal(value, CloudflareZones.Unquote(content));

    // The edge is configured only when every name the runtime needs is there, and its token is for the endpoint it calls.
    [Fact]
    public void The_edge_is_configured_only_with_every_name_it_needs()
    {
        var options = new EdgeDomainOptions
        {
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            Profile = "fd",
            Endpoint = "endpoint",
            EndpointHost = "endpoint.z01.azurefd.net",
            WafPolicyId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/frontdoorWebApplicationFirewallPolicies/waf",
        };

        Assert.True(options.IsConfigured);
        Assert.False(options.IsPartlyConfigured);
        Assert.False(new EdgeDomainOptions().IsConfigured);
        Assert.False(new EdgeDomainOptions().IsPartlyConfigured);
        Assert.False(new EdgeDomainOptions { SubscriptionId = "sub", ResourceGroup = "rg", Profile = "fd", Endpoint = "e", EndpointHost = "h" }.IsConfigured);
        Assert.True(new EdgeDomainOptions { SubscriptionId = "sub", ResourceGroup = "rg", Profile = "fd", Endpoint = "e", EndpointHost = "h" }.IsPartlyConfigured);
        Assert.Equal("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Cdn/profiles/fd", options.ProfileId);
        Assert.Equal("https://management.azure.com/.default", options.TokenScope);
        Assert.Equal("http://127.0.0.1:5000/.default", new EdgeDomainOptions { ManagementUrl = "http://127.0.0.1:5000/" }.TokenScope);
    }

    [Fact]
    public void Cloudflare_is_configured_only_with_a_token_and_an_account()
    {
        Assert.True(new CloudflareOptions { ApiToken = "token", AccountId = "account" }.IsConfigured);
        Assert.False(new CloudflareOptions { ApiToken = "token" }.IsConfigured);
        Assert.False(new CloudflareOptions { AccountId = "account" }.IsConfigured);
        Assert.True(new CloudflareOptions { ApiToken = "token" }.IsPartlyConfigured);
        Assert.True(new CloudflareOptions { AccountId = "account" }.IsPartlyConfigured);
        Assert.False(new CloudflareOptions().IsPartlyConfigured);
    }

    private static EdgeDomain Domain(
        EdgeValidation validation,
        string? token,
        DateTimeOffset? expires,
        EdgeProvisioning provisioning = EdgeProvisioning.Succeeded,
        EdgeDeployment deployment = EdgeDeployment.NotStarted,
        bool issued = false) =>
        new("www.shop.cz", "m-www-shop-cz-0123456789", validation, token, expires, provisioning, deployment, issued);

    // What Resource Manager accepts as a profile's child: letters and digits, with hyphens between them.
    [GeneratedRegex("^[a-zA-Z0-9]+(-*[a-zA-Z0-9])*$")]
    private static partial Regex ResourceName();
}
