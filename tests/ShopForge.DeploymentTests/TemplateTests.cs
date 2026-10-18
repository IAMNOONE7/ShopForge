using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShopForge.DeploymentTests;

// The templates as Azure would receive them, compiled and linted by the compiler the Azure CLI runs, with no
// subscription involved. The order Azure creates things in is the dependency graph compiled here, which is the part
// of `what-if` that needs no account (D-205).
public sealed partial class TemplateTests
{
    private const string Main = "infra/main.bicep";

    private static readonly Dictionary<string, string?> SampleSecrets = new()
    {
        ["SHOPFORGE_DB_PASSWORD"] = "made-up",
        ["SHOPFORGE_EMAIL_SENDER"] = "shop@mail.example.cz",
        ["SHOPFORGE_MAILGUN_DOMAIN"] = "mail.example.cz",
        ["SHOPFORGE_MAILGUN_API_KEY"] = "made-up",
        ["SHOPFORGE_MAILGUN_WEBHOOK_SIGNING_KEY"] = "made-up",
    };

    [Fact]
    public async Task The_templates_compile_and_lint_clean_and_the_sample_parameters_fit_them()
    {
        var lint = await Repository.RunAsync(Repository.BicepPath, ["lint", Main]);
        var sample = await Repository.RunAsync(Repository.BicepPath, ["build-params", "infra/main.sample.bicepparam", "--stdout"], SampleSecrets);

        Assert.Equal((0, ""), (lint.ExitCode, (lint.Output + lint.Errors).Trim()));
        Assert.True(sample.ExitCode == 0, sample.Errors);
    }

    // The app and the job that migrates for it are made only once they may pull their image and read their vault,
    // and the environment they run in exists: nothing starts and then waits for a role that waits for it.
    [Theory]
    [InlineData("api")]
    [InlineData("migrations")]
    public async Task The_app_and_its_migration_job_come_after_everything_they_need(string deployment)
    {
        var template = await CompiledAsync();

        var after = DependsOn(Deployment(template, deployment));

        Assert.Superset(new HashSet<string> { "identity", "registry-access", "vault", "environment" }, after);
    }

    // Both run as the identity those roles were granted to, which is the app's own and not one the app brings with it.
    [Theory]
    [InlineData("api", "Microsoft.App/containerApps")]
    [InlineData("migrations", "Microsoft.App/jobs")]
    public async Task The_app_and_its_migration_job_run_as_the_identity_granted_their_roles(string deployment, string type)
    {
        var template = await CompiledAsync();
        var module = Deployment(template, deployment);

        var resource = Resources(module["properties"]!["template"]!).Single(candidate => (string?)candidate["type"] == type);
        var settings = module["properties"]!["parameters"]!["settings"]!["value"]!.AsArray();

        Assert.Equal("UserAssigned", (string?)resource["identity"]!["type"]);
        Assert.Equal("[parameters('identityId')]", (string?)resource["properties"]!["configuration"]!["registries"]![0]!["identity"]);
        Assert.Matches(OutputOf("identity", "id"), Parameter(module, "identityId"));
        Assert.Matches(OutputOf("identity", "clientId"), Setting(settings, "AZURE_CLIENT_ID"));
        Assert.Matches(OutputOf("identity", "principalId"), Parameter(Deployment(template, "vault"), "appPrincipalId"));
        Assert.Matches(OutputOf("identity", "principalId"), Parameter(Deployment(template, "registry-access"), "principalId"));
    }

    // The first of a release's two deployments makes everything but the app, the migration job with the new image
    // among it; only the second touches what is serving (D-205).
    [Fact]
    public async Task A_release_s_first_deployment_leaves_the_serving_app_alone()
    {
        var template = await CompiledAsync();

        Assert.Equal("[parameters('deployApp')]", (string?)Deployment(template, "api")["condition"]);
        Assert.Equal("[parameters('deployApp')]", (string?)Deployment(template, "edge-routes")["condition"]);
        Assert.Null(Deployment(template, "migrations")["condition"]);
        Assert.Equal("[parameters('image')]", Parameter(Deployment(template, "migrations"), "image"));
    }

    // The job is the build itself told to migrate, and it is told everything the app is told, so it finds the same
    // database the same way.
    [Fact]
    public async Task The_migration_job_runs_the_release_s_build_told_what_the_app_is_told()
    {
        var template = await CompiledAsync();
        var job = Resources(Deployment(template, "migrations")["properties"]!["template"]!).Single();
        var container = job["properties"]!["template"]!["containers"]![0]!;

        Assert.Equal("Manual", (string?)job["properties"]!["configuration"]!["triggerType"]);
        Assert.Equal(0, (int?)job["properties"]!["configuration"]!["replicaRetryLimit"]);
        Assert.Equal(["migrate"], container["args"]!.AsArray().Select(argument => (string?)argument));
        Assert.Equal("[parameters('settings')]", (string?)container["env"]);
        Assert.True(JsonNode.DeepEquals(
            Deployment(template, "api")["properties"]!["parameters"]!["settings"],
            Deployment(template, "migrations")["properties"]!["parameters"]!["settings"]));
    }

    // A deployment always sends mail: only a provider that delivers is accepted, and both of Mailgun's keys are
    // required and kept in the vault (D-205).
    [Fact]
    public async Task A_deployment_has_to_send_mail()
    {
        var template = await CompiledAsync();
        var parameters = template["parameters"]!;
        var vault = Resources(Deployment(template, "vault")["properties"]!["template"]!);

        Assert.Equal(["mailgun"], parameters["emailProvider"]!["allowedValues"]!.AsArray().Select(value => (string?)value));
        Assert.All(
            new[] { "mailgunApiKey", "mailgunWebhookSigningKey" },
            name => Assert.Equal(("securestring", (JsonNode?)null), ((string?)parameters[name]!["type"], parameters[name]!["defaultValue"])));
        Assert.All(
            new[] { "Email--Mailgun--ApiKey", "Email--Mailgun--WebhookSigningKey" },
            secret => Assert.Contains(vault, resource =>
                ((string?)resource["name"])?.Contains($"'{secret}'", StringComparison.Ordinal) == true && resource["condition"] is null));
    }

    // Everything reaches the app through one route to one origin, so the pages and the API share an address (D-206),
    // and plain HTTP is turned to HTTPS at the edge.
    [Fact]
    public async Task The_edge_sends_everything_to_the_app_through_one_route()
    {
        var template = await CompiledAsync();
        var edge = Resources(Deployment(template, "edge-routes")["properties"]!["template"]!).ToList();
        var route = edge.Single(resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/afdEndpoints/routes")["properties"]!;

        Assert.Single(edge, resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/originGroups");
        Assert.Single(edge, resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/originGroups/origins");
        Assert.Equal(["/*"], route["patternsToMatch"]!.AsArray().Select(pattern => (string?)pattern));
        Assert.Equal(["Http", "Https"], route["supportedProtocols"]!.AsArray().Select(protocol => (string?)protocol));
        Assert.Equal(("Enabled", "HttpsOnly"), ((string?)route["httpsRedirect"], (string?)route["forwardingProtocol"]));
        Assert.Null(route["cacheConfiguration"]);
    }

    // Only files whose names change with their content are cached at the edge, and for as long as the app says; a page
    // or an answer from the API never is (D-206).
    [Fact]
    public async Task Only_hashed_assets_are_cached_at_the_edge()
    {
        var template = await CompiledAsync();
        var rule = Resources(Deployment(template, "edge-routes")["properties"]!["template"]!)
            .Single(resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/ruleSets/rules")["properties"]!;
        var condition = rule["conditions"]![0]!["parameters"]!;
        var cache = rule["actions"]![0]!["parameters"]!["cacheConfiguration"]!;

        Assert.Equal(("BeginsWith", false), ((string?)condition["operator"], (bool?)condition["negateCondition"]));
        Assert.Equal(["/assets/", "/admin/assets/"], condition["matchValues"]!.AsArray().Select(value => (string?)value));
        Assert.Equal("RouteConfigurationOverride", (string?)rule["actions"]![0]!["name"]);
        Assert.Equal("HonorOrigin", (string?)cache["cacheBehavior"]);
    }

    // The template owns the platform's route, its security policy and its own domains; every merchant's domain, and the
    // route and policy that carry them, belong to the runtime. A redeploy can then never unbind a shop (D-207).
    [Fact]
    public async Task The_template_declares_no_domain_route_or_policy_but_the_platform_s()
    {
        var template = await CompiledAsync();
        var everything = Everywhere(template).ToList();

        Assert.Equal(["platform"], OwnNames(everything, "Microsoft.Cdn/profiles/afdEndpoints/routes"));
        Assert.Equal(["platform"], OwnNames(everything, "Microsoft.Cdn/profiles/securityPolicies"));
        Assert.Equal(["admin"], OwnNames(everything, "Microsoft.Cdn/profiles/customDomains"));
    }

    // A platform domain with a managed certificate, which Front Door issues once _dnsauth.<host> carries the token the
    // deployment hands out. Without one, the admin is served on the endpoint's own host name (D-206).
    [Fact]
    public async Task The_admin_domain_is_the_template_s_and_its_proof_is_handed_out()
    {
        var template = await CompiledAsync();
        var profile = Deployment(template, "edge-profile")["properties"]!["template"]!;
        var domain = Resources(profile).Single(resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/customDomains");

        Assert.Equal("[not(empty(parameters('adminHost')))]", (string?)domain["condition"]);
        Assert.Equal(("ManagedCertificate", "TLS12"), ((string?)domain["properties"]!["tlsSettings"]!["certificateType"], (string?)domain["properties"]!["tlsSettings"]!["minimumTlsVersion"]));
        Assert.Contains("validationProperties.validationToken", (string?)profile["outputs"]!["adminValidationToken"]!["value"], StringComparison.Ordinal);
        Assert.NotNull(template["outputs"]!["adminValidationToken"]);
        Assert.Matches(OutputOf("edge-profile", "adminHost"), Setting(Deployment(template, "api")["properties"]!["parameters"]!["settings"]!["value"]!.AsArray(), "Frontends__AdminHost"));
    }

    // Detection until V19 has seen real traffic; the endpoint's own domain and the platform's are covered from the start.
    [Fact]
    public async Task The_firewall_is_defined_in_detection_over_the_platform_s_domains()
    {
        var template = await CompiledAsync();
        var security = Deployment(template, "edge-security");
        var resources = Resources(security["properties"]!["template"]!).ToList();
        var policy = resources.Single(resource => (string?)resource["type"] == "Microsoft.Network/FrontDoorWebApplicationFirewallPolicies");
        var association = resources.Single(resource => (string?)resource["type"] == "Microsoft.Cdn/profiles/securityPolicies")["properties"]!["parameters"]!["associations"]![0]!;

        Assert.Equal(("Detection", "Standard_AzureFrontDoor"), ((string?)policy["properties"]!["policySettings"]!["mode"], (string?)policy["sku"]!["name"]));
        Assert.Equal(["/*"], association["patternsToMatch"]!.AsArray().Select(pattern => (string?)pattern));
        Assert.Contains("outputs.endpointId.value", Parameter(security, "domainIds"), StringComparison.Ordinal);
        Assert.Contains("outputs.platformDomainIds.value", Parameter(security, "domainIds"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("param emailProvider = 'log'", "BCP033")]
    [InlineData(null, "BCP258")]
    public async Task Parameters_that_would_send_no_mail_are_refused_before_anything_is_deployed(string? extra, string diagnostic)
    {
        var directory = Directory.CreateTempSubdirectory("shopforge-params-");

        try
        {
            var file = Path.Combine(directory.FullName, "release.bicepparam");
            var lines = new List<string>
            {
                $"using '{Path.GetRelativePath(directory.FullName, Repository.PathOf(Main)).Replace('\\', '/')}'",
                "param namePrefix = 'shopforge-test'",
                "param image = 'registry.azurecr.io/shopforge-api:1'",
                "param registryName = 'registry'",
                "param registryResourceGroup = 'shared'",
                "param storeHostSuffix = 'shops.example.cz'",
                "param databasePassword = 'made-up'",
                "param emailSenderAddress = 'shop@mail.example.cz'",
                "param mailgunDomain = 'mail.example.cz'",
                "param mailgunApiKey = 'made-up'",
            };

            // Without an extra line the signing key is what is missing.
            if (extra is not null)
            {
                lines.Add("param mailgunWebhookSigningKey = 'made-up'");
                lines.Add(extra);
            }

            await File.WriteAllLinesAsync(file, lines, TestContext.Current.CancellationToken);

            var built = await Repository.RunAsync(Repository.BicepPath, ["build-params", file, "--stdout"]);

            Assert.NotEqual(0, built.ExitCode);
            Assert.Contains(diagnostic, built.Errors, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // Compiled once for the class: every test reads it and none changes it.
    private static readonly Lazy<Task<JsonNode>> Compiled = new(async () =>
    {
        var built = await Repository.RunAsync(Repository.BicepPath, ["build", Main, "--stdout"]);
        Assert.True(built.ExitCode == 0, built.Errors);

        return JsonNode.Parse(built.Output)!;
    });

    private static Task<JsonNode> CompiledAsync() => Compiled.Value;

    // Every resource in the template and in every module it deploys, however deep.
    private static IEnumerable<JsonNode> Everywhere(JsonNode template) =>
        Resources(template).SelectMany(resource => resource["properties"]?["template"] is { } nested
            ? Everywhere(nested).Prepend(resource)
            : [resource]);

    private static List<string> OwnNames(IEnumerable<JsonNode> resources, string type) =>
        [.. resources.Where(resource => (string?)resource["type"] == type)
            .Select(resource => OwnName().Match((string)resource["name"]!).Groups["name"].Value)];

    private static IEnumerable<JsonNode> Resources(JsonNode template) =>
        template["resources"] switch
        {
            JsonArray array => array.Select(resource => resource!),
            JsonObject keyed => keyed.Select(pair => pair.Value!),
            _ => [],
        };

    // A module's deployment, found by the name it is given in main.bicep (a prefix such as the environment's is ignored).
    private static JsonNode Deployment(JsonNode template, string name) =>
        Resources(template).Single(resource =>
            (string?)resource["type"] == "Microsoft.Resources/deployments" && DeploymentName((string)resource["name"]!) == name);

    private static HashSet<string> DependsOn(JsonNode deployment) =>
        [.. (deployment["dependsOn"]?.AsArray() ?? []).Select(entry => DeploymentReference().Match((string)entry!))
            .Where(match => match.Success)
            .Select(match => DeploymentName(match.Groups["name"].Value))];

    private static string DeploymentName(string name) => DeploymentNamePattern().Match(name) is { Success: true } formatted
        ? formatted.Groups["suffix"].Value
        : name.Trim('\'');

    private static string Parameter(JsonNode deployment, string name) =>
        (string)deployment["properties"]!["parameters"]![name]!["value"]!;

    private static string Setting(JsonArray settings, string name) =>
        (string)settings.Single(setting => (string?)setting!["name"] == name)!["value"]!;

    private static string OutputOf(string deployment, string output) =>
        $"'Microsoft\\.Resources/deployments', '{deployment}'\\), '[0-9-]+'\\)\\.outputs\\.{output}\\.value";

    // `[format('{0}/{1}/{2}', parameters('profileName'), parameters('endpointName'), 'platform')]` is the route named platform.
    [GeneratedRegex(@"'(?<name>[A-Za-z0-9-]+)'\)\]$")]
    private static partial Regex OwnName();

    [GeneratedRegex(@"'Microsoft\.Resources/deployments', (?<name>'[^']+'|format\([^)]*\)[^)]*\))\)\]$")]
    private static partial Regex DeploymentReference();

    // `[format('{0}-registry-access', parameters('namePrefix'))]` is the deployment main.bicep calls registry-access.
    [GeneratedRegex(@"format\('\{0\}-(?<suffix>[a-z-]+)'")]
    private static partial Regex DeploymentNamePattern();
}
