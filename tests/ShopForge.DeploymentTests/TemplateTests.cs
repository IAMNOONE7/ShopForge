using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShopForge.DeploymentTests;

// The templates as Azure would receive them, compiled and linted by the compiler the Azure CLI runs, with no
// subscription involved. The order Azure creates things in is the dependency graph compiled here, which is the part
// of `what-if` that needs no account (D-205).
public sealed partial class TemplateTests
{
    private const string Main = "infra/main.bicep";
    private const string Bootstrap = "infra/bootstrap.bicep";

    private static readonly string[] Environments = ["staging", "production"];

    // What a GitHub environment hands a release, made up; every one of them is read by the parameter files (D-208).
    private static readonly Dictionary<string, string?> FromTheEnvironment = new()
    {
        ["SHOPFORGE_IMAGE"] = "shopforgeshared.azurecr.io/shopforge-api:0123456",
        ["SHOPFORGE_REGISTRY_NAME"] = "shopforgeshared",
        ["SHOPFORGE_REGISTRY_RESOURCE_GROUP"] = "shopforge-shared-rg",
        ["SHOPFORGE_STORE_HOST_SUFFIX"] = "shops.example.cz",
        ["SHOPFORGE_ADMIN_HOST"] = "admin.example.cz",
        ["SHOPFORGE_FIRST_OPERATOR"] = "ops@example.cz",
        ["SHOPFORGE_EMAIL_SENDER"] = "shop@mail.example.cz",
        ["SHOPFORGE_MAILGUN_DOMAIN"] = "mail.example.cz",
        ["SHOPFORGE_DB_PASSWORD"] = "made-up",
        ["SHOPFORGE_MAILGUN_API_KEY"] = "made-up",
        ["SHOPFORGE_MAILGUN_WEBHOOK_SIGNING_KEY"] = "made-up",
    };

    [Theory]
    [InlineData(Main)]
    [InlineData(Bootstrap)]
    public async Task The_templates_compile_and_lint_clean(string template)
    {
        var lint = await Repository.RunAsync(Repository.BicepPath, ["lint", template]);

        Assert.Equal((0, ""), (lint.ExitCode, (lint.Output + lint.Errors).Trim()));
    }

    // Two environments of one topology: what tells them apart is their name and their sizes, and anything else is the
    // same expression read from each GitHub environment (D-208).
    [Fact]
    public async Task Staging_and_production_differ_in_name_and_size_alone()
    {
        var staging = await EnvironmentParametersAsync("staging");
        var production = await EnvironmentParametersAsync("production");

        var different = staging.Select(pair => pair.Key)
            .Where(name => !JsonNode.DeepEquals(staging[name]!["value"], production[name]!["value"]))
            .Order(StringComparer.Ordinal);

        Assert.Equal(staging.Select(pair => pair.Key).Order(StringComparer.Ordinal), production.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Subset(
            new HashSet<string> { "namePrefix", "databaseSkuName", "databaseTier", "databaseStorageGb", "databaseBackupDays", "appMinReplicas", "appMaxReplicas" },
            different.ToHashSet());
        Assert.Contains("namePrefix", different);
    }

    // A secret goes into a deployment from the environment that runs it, never from a file in the repository.
    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public async Task An_environment_s_file_holds_no_secret(string environment)
    {
        var template = await CompiledAsync();
        var file = await File.ReadAllTextAsync(Repository.PathOf($"infra/environments/{environment}.bicepparam"), TestContext.Current.CancellationToken);
        var secrets = template["parameters"]!.AsObject()
            .Where(parameter => (string?)parameter.Value!["type"] == "securestring")
            .Select(parameter => parameter.Key)
            .ToList();

        Assert.NotEmpty(secrets);
        Assert.All(secrets, secret => Assert.Matches($@"(?m)^param {secret} = readEnvironmentVariable\('SHOPFORGE_[A-Z_]+'", file));
    }

    // Who read which secret, what the database and the edge did: each environment keeps all of it beside its traces.
    [Theory]
    [InlineData("vault", 2, "audit")]
    [InlineData("data", 1, "allLogs")]
    [InlineData("edge-profile", 1, "allLogs")]
    public async Task Every_environment_keeps_its_logs(string deployment, int count, string categoryGroup)
    {
        var template = await CompiledAsync();
        var module = Deployment(template, deployment);

        var diagnostics = Resources(module["properties"]!["template"]!)
            .Where(resource => (string?)resource["type"] == "Microsoft.Insights/diagnosticSettings")
            .ToList();

        Assert.Equal(count, diagnostics.Count);
        Assert.All(diagnostics, setting =>
        {
            Assert.Null(setting["condition"]);
            Assert.Equal("[parameters('workspaceId')]", (string?)setting["properties"]!["workspaceId"]);
            Assert.Equal(categoryGroup, (string?)setting["properties"]!["logs"]![0]!["categoryGroup"]);
        });
        Assert.Matches(OutputOf("observability", "workspaceId"), Parameter(module, "workspaceId"));
    }

    [Fact]
    public async Task An_environment_s_sizes_reach_its_resources()
    {
        var template = await CompiledAsync();
        var data = Deployment(template, "data");
        var api = Deployment(template, "api");
        var server = Resources(data["properties"]!["template"]!).Single(resource => (string?)resource["type"] == "Microsoft.DBforPostgreSQL/flexibleServers");
        var app = Resources(api["properties"]!["template"]!).Single(resource => (string?)resource["type"] == "Microsoft.App/containerApps");

        Assert.Equal(("[parameters('databaseSkuName')]", "[parameters('appMinReplicas')]"), (Parameter(data, "skuName"), Parameter(api, "minReplicas")));
        Assert.Equal("[parameters('skuName')]", (string?)server["sku"]!["name"]);
        Assert.Equal("[parameters('backupDays')]", (string?)server["properties"]!["backup"]!["backupRetentionDays"]);
        Assert.Equal("[parameters('maxReplicas')]", (string?)app["properties"]!["template"]!["scale"]!["maxReplicas"]);
    }

    // The bootstrap's deployer may assign and remove exactly the roles a release assigns, and no other: a stolen
    // workflow token cannot make itself Owner of the subscription (D-208). The edge domains role is the bootstrap's own,
    // its id derived the same way in both templates (D-212).
    [Fact]
    public async Task The_deployer_may_grant_exactly_what_a_release_grants()
    {
        var bootstrap = await BootstrapAsync();
        var main = await CompiledAsync();
        var assignments = Resources(bootstrap).Where(resource => (string?)resource["type"] == "Microsoft.Authorization/roleAssignments").ToList();
        var granting = assignments.Single(assignment => assignment["properties"]!["condition"] is not null)["properties"]!;
        var grantable = RoleKeys().Matches(Evaluated((string)granting["condition"]!, bootstrap)).Select(match => match.Value).ToHashSet();
        var granted = Granted(main).ToHashSet();

        Assert.Equal("2.0", (string?)granting["conditionVersion"]);
        Assert.Equal("f58310d9-a9f6-439a-9e8d-f62e7b41a168", RoleOf(granting, bootstrap));
        Assert.Equal(granted.Order(), grantable.Order());
        Assert.Contains(EdgeDomainsRole, granted);
        Assert.Contains(assignments, assignment => RoleOf(assignment["properties"]!, bootstrap) == "b24988ac-6180-42a0-ab88-20f7382dd24c");
    }

    // What the app may do at the edge: merchants' domains, the runtime's route and policy, reading what they refer to, and
    // joining the firewall policy. It may not change or delete the profile, nor the firewall policy's rules, and it is
    // granted over the environment's resource group alone (D-212).
    [Fact]
    public async Task The_edge_domains_role_reaches_merchants_domains_and_nothing_it_could_break()
    {
        var bootstrap = await BootstrapAsync();
        var main = await CompiledAsync();
        var role = Resources(bootstrap).Single(resource => (string?)resource["type"] == "Microsoft.Authorization/roleDefinitions");
        var actions = role["properties"]!["permissions"]![0]!["actions"]!.AsArray().Select(action => (string)action!).ToList();
        var access = Deployment(main, "edge-access");
        var assignment = Resources(access["properties"]!["template"]!).Single();

        Assert.Equal("[variables('edgeDomains')]", (string?)role["name"]);
        Assert.Equal($"[{EdgeDomainsRole}]", (string?)bootstrap["variables"]!["edgeDomains"]);
        Assert.Equal("CustomRole", (string?)role["properties"]!["type"]);
        Assert.Equal(["[subscription().id]"], role["properties"]!["assignableScopes"]!.AsArray().Select(scope => (string?)scope));
        Assert.All(actions, action => Assert.True(
            action.StartsWith("Microsoft.Cdn/profiles/", StringComparison.Ordinal)
                || action.StartsWith("Microsoft.Cdn/operationresults/", StringComparison.Ordinal)
                || action.StartsWith("Microsoft.Network/frontDoorWebApplicationFirewallPolicies/", StringComparison.Ordinal),
            action));
        Assert.DoesNotContain(actions, action => action.EndsWith('*'));
        Assert.DoesNotContain(actions, action => action is "Microsoft.Cdn/profiles/write" or "Microsoft.Cdn/profiles/delete");
        Assert.DoesNotContain(actions, action => action.StartsWith("Microsoft.Network/", StringComparison.Ordinal) && !action.EndsWith("/read", StringComparison.Ordinal) && !action.EndsWith("/join/action", StringComparison.Ordinal));
        Assert.Contains("Microsoft.Network/frontDoorWebApplicationFirewallPolicies/join/action", actions);
        Assert.Null(assignment["scope"]);
        Assert.Equal("[format('{0}-rg', parameters('namePrefix'))]", (string?)access["resourceGroup"]);
        Assert.Matches(OutputOf("identity", "principalId"), Parameter(access, "principalId"));
    }

    // The roles a release assigns, however deep: a built-in role's id, or the expression a role of the bootstrap's is
    // derived from, read through the module parameter that carries it.
    private static IEnumerable<string> Granted(JsonNode template)
    {
        foreach (var resource in Resources(template))
        {
            if ((string?)resource["type"] == "Microsoft.Authorization/roleAssignments")
            {
                yield return RoleKeys().Match((string)resource["properties"]!["roleDefinitionId"]!).Value;
            }
            else if ((string?)resource["type"] == "Microsoft.Resources/deployments" && resource["properties"]?["template"] is { } nested)
            {
                foreach (var role in Granted(nested))
                {
                    yield return role.Length > 0 ? role : ModuleRole(resource);
                }
            }
        }
    }

    // A module handed its role as a parameter, as edge-access is.
    private static string ModuleRole(JsonNode deployment) =>
        RoleKeys().Match((string?)deployment["properties"]!["parameters"]!["roleId"]?["value"] ?? string.Empty).Value;

    private static JsonNode Domain(JsonNode template, string name) =>
        Resources(template).Single(resource =>
            (string?)resource["type"] == "Microsoft.Cdn/profiles/customDomains" && OwnName().Match((string)resource["name"]!).Groups["name"].Value == name);

    // A role assignment's role, written as an id or as one of the template's variables.
    private static string RoleOf(JsonNode properties, JsonNode template)
    {
        var expression = (string)properties["roleDefinitionId"]!;

        return Variable().Match(expression) is { Success: true } variable
            ? (string)template["variables"]![variable.Groups["name"].Value]!
            : RoleIds().Match(expression).Value;
    }

    // No secret exists for the deployer: GitHub proves which repository and environment a job runs in, one environment
    // per credential.
    [Fact]
    public async Task Only_this_repository_s_environments_may_sign_in_as_the_deployer()
    {
        var bootstrap = await BootstrapAsync();
        var deployer = Resources(Deployment(bootstrap, "deployer")["properties"]!["template"]!).ToList();
        var credential = deployer.Single(resource => (string?)resource["type"] == "Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials");

        Assert.Equal(("serial", 1), ((string?)credential["copy"]!["mode"], (int?)credential["copy"]!["batchSize"]));
        Assert.Equal("https://token.actions.githubusercontent.com", (string?)credential["properties"]!["issuer"]);
        Assert.Equal("[format('repo:{0}:environment:{1}', parameters('repository'), parameters('environments')[copyIndex()])]", (string?)credential["properties"]!["subject"]);
        Assert.Equal(["api://AzureADTokenExchange"], credential["properties"]!["audiences"]!.AsArray().Select(audience => (string?)audience));
        Assert.Equal(Environments, bootstrap["parameters"]!["environments"]!["defaultValue"]!.AsArray().Select(environment => (string?)environment));
    }

    [Fact]
    public async Task The_registry_has_no_password_and_only_the_deployer_pushes()
    {
        var bootstrap = await BootstrapAsync();
        var registry = Resources(Deployment(bootstrap, "registry")["properties"]!["template"]!).ToList();
        var push = registry.Single(resource => (string?)resource["type"] == "Microsoft.Authorization/roleAssignments")["properties"]!;

        Assert.Equal(false, (bool?)registry.Single(resource => (string?)resource["type"] == "Microsoft.ContainerRegistry/registries")["properties"]!["adminUserEnabled"]);
        Assert.Contains("8311e382-0749-4cb8-b61a-304f252e45ec", (string?)push["roleDefinitionId"], StringComparison.Ordinal);
        Assert.Equal("[parameters('pusherPrincipalId')]", (string?)push["principalId"]);
        Assert.Matches(OutputOf("deployer", "principalId"), Parameter(Deployment(bootstrap, "registry"), "pusherPrincipalId"));
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
        Assert.Equal(["admin", "stores"], OwnNames(everything, "Microsoft.Cdn/profiles/customDomains").Order(StringComparer.Ordinal));
    }

    // Every store's own subdomain arrives through one wildcard domain of the template's with a managed certificate, on the
    // platform's route and under its policy, its token handed out as the admin's is (D-212).
    [Fact]
    public async Task Store_subdomains_are_served_through_one_wildcard_domain_of_the_template_s()
    {
        var template = await CompiledAsync();
        var profile = Deployment(template, "edge-profile");
        var stores = Domain(profile["properties"]!["template"]!, "stores");
        var platformDomains = (string)profile["properties"]!["template"]!["outputs"]!["platformDomainIds"]!["value"]!;

        Assert.Null(stores["condition"]);
        Assert.Equal("[format('*.{0}', parameters('storeHostSuffix'))]", (string?)stores["properties"]!["hostName"]);
        Assert.Equal(("ManagedCertificate", "TLS12"), ((string?)stores["properties"]!["tlsSettings"]!["certificateType"], (string?)stores["properties"]!["tlsSettings"]!["minimumTlsVersion"]));
        Assert.Contains("'stores')", platformDomains, StringComparison.Ordinal);
        Assert.Equal("[parameters('storeHostSuffix')]", Parameter(profile, "storeHostSuffix"));
        Assert.NotNull(template["outputs"]!["storesValidationToken"]);
    }

    // The app is told where merchants' domains are bound: the profile, its endpoint and the firewall policy the runtime's
    // security policy applies; and, only where the platform runs DNS, the Cloudflare account and a token kept in the vault
    // (D-212). The origin group and rule set the runtime's route reuses are edge-routes' own names, which are the app's
    // defaults.
    [Fact]
    public async Task The_app_is_told_where_to_bind_merchants_domains()
    {
        var template = await CompiledAsync();
        var settings = Deployment(template, "api")["properties"]!["parameters"]!["settings"]!["value"]!.AsArray();
        var vault = Resources(Deployment(template, "vault")["properties"]!["template"]!);
        var routes = Resources(Deployment(template, "edge-routes")["properties"]!["template"]!).ToList();
        var token = template["parameters"]!["cloudflareApiToken"]!;

        Assert.Equal("[subscription().subscriptionId]", Setting(settings, "Edge__Domains__SubscriptionId"));
        Assert.Matches(OutputOf("edge-profile", "profileName"), Setting(settings, "Edge__Domains__Profile"));
        Assert.Matches(OutputOf("edge-profile", "endpointName"), Setting(settings, "Edge__Domains__Endpoint"));
        Assert.Matches(OutputOf("edge-profile", "endpointHostName"), Setting(settings, "Edge__Domains__EndpointHost"));
        Assert.Matches(OutputOf("edge-security", "wafPolicyId"), Setting(settings, "Edge__Domains__WafPolicyId"));
        Assert.Equal("[parameters('cloudflareAccountId')]", Setting(settings, "Dns__Cloudflare__AccountId"));
        Assert.Equal(("securestring", ""), ((string?)token["type"], (string?)token["defaultValue"]));
        Assert.Contains(vault, resource =>
            ((string?)resource["name"])?.Contains("'Dns--Cloudflare--ApiToken'", StringComparison.Ordinal) == true
            && (string?)resource["condition"] == "[not(empty(parameters('cloudflareApiToken')))]");
        Assert.Equal(["app"], OwnNames(routes, "Microsoft.Cdn/profiles/originGroups"));
        Assert.Equal(["caching"], OwnNames(routes, "Microsoft.Cdn/profiles/ruleSets"));
    }

    // A platform domain with a managed certificate, which Front Door issues once _dnsauth.<host> carries the token the
    // deployment hands out. The admin is on the endpoint's own host name as well, with or without one (D-206, D-208).
    [Fact]
    public async Task The_admin_domain_is_the_template_s_and_its_proof_is_handed_out()
    {
        var template = await CompiledAsync();
        var profile = Deployment(template, "edge-profile")["properties"]!["template"]!;
        var domain = Domain(profile, "admin");
        var settings = Deployment(template, "api")["properties"]!["parameters"]!["settings"]!["value"]!.AsArray();

        Assert.Equal("[not(empty(parameters('adminHost')))]", (string?)domain["condition"]);
        Assert.Equal(("ManagedCertificate", "TLS12"), ((string?)domain["properties"]!["tlsSettings"]!["certificateType"], (string?)domain["properties"]!["tlsSettings"]!["minimumTlsVersion"]));
        Assert.Contains("validationProperties.validationToken", (string?)profile["outputs"]!["adminValidationToken"]!["value"], StringComparison.Ordinal);
        Assert.NotNull(template["outputs"]!["adminValidationToken"]);
        Assert.Matches(OutputOf("edge-profile", "adminHost"), Setting(settings, "Frontends__AdminHost"));
        Assert.Matches(OutputOf("edge-profile", "endpointHostName"), Setting(settings, "Frontends__EdgeHost"));
    }

    // The release's migration job is told who first runs the platform, and invites them while nobody does (D-208).
    [Fact]
    public async Task The_migration_job_is_told_who_first_runs_the_platform()
    {
        var template = await CompiledAsync();
        var settings = Deployment(template, "migrations")["properties"]!["parameters"]!["settings"]!["value"]!.AsArray();

        Assert.Equal("[parameters('firstOperatorEmail')]", Setting(settings, "Platform__FirstOperator"));
        Assert.Equal("", (string?)template["parameters"]!["firstOperatorEmail"]!["defaultValue"]);
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

    private static async Task<JsonObject> EnvironmentParametersAsync(string environment)
    {
        var built = await Repository.RunAsync(Repository.BicepPath, ["build-params", $"infra/environments/{environment}.bicepparam", "--stdout"], FromTheEnvironment);
        Assert.True(built.ExitCode == 0, built.Errors);

        return JsonNode.Parse((string)JsonNode.Parse(built.Output)!["parametersJson"]!)!["parameters"]!.AsObject();
    }

    private static readonly Lazy<Task<JsonNode>> CompiledBootstrap = new(async () =>
    {
        var built = await Repository.RunAsync(Repository.BicepPath, ["build", Bootstrap, "--stdout"]);
        Assert.True(built.ExitCode == 0, built.Errors);

        return JsonNode.Parse(built.Output)!;
    });

    private static Task<JsonNode> BootstrapAsync() => CompiledBootstrap.Value;

    // A condition is a `format()` the deployment evaluates; this evaluates it the same way, its arguments being the
    // `join()` of a variable that ARM would compute. An item that is itself a variable stands for that variable's value.
    private static string Evaluated(string expression, JsonNode template)
    {
        var variables = template["variables"]!.AsObject();
        var call = Format().Match(expression);

        if (!call.Success)
        {
            return expression;
        }

        var arguments = Join().Matches(call.Groups["arguments"].Value)
            .Select(match => string.Join(
                match.Groups["separator"].Value,
                variables[match.Groups["variable"].Value]!.AsArray().Select(value => Variable().Match((string)value!) is { Success: true } reference
                    ? (string?)variables[reference.Groups["name"].Value]
                    : (string?)value)))
            .ToArray();

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, call.Groups["format"].Value.Replace("''", "'", StringComparison.Ordinal), arguments);
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

    [GeneratedRegex(@"variables\('(?<name>[a-zA-Z]+)'\)")]
    private static partial Regex Variable();

    [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
    private static partial Regex RoleIds();

    // A built-in role's id, or a role of the bootstrap's own written as the expression its id is derived from.
    [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|guid\(subscription\(\)\.id, '[a-z-]+'\)")]
    private static partial Regex RoleKeys();

    private const string EdgeDomainsRole = "guid(subscription().id, 'shopforge-edge-domains')";

    [GeneratedRegex(@"^\[format\('(?<format>(?:[^']|'')*)', (?<arguments>.*)\)\]$")]
    private static partial Regex Format();

    [GeneratedRegex(@"join\(variables\('(?<variable>[a-zA-Z]+)'\), '(?<separator>[^']*)'\)")]
    private static partial Regex Join();

    [GeneratedRegex(@"'Microsoft\.Resources/deployments', (?<name>'[^']+'|format\([^)]*\)[^)]*\))\)\]$")]
    private static partial Regex DeploymentReference();

    // `[format('{0}-registry-access', parameters('namePrefix'))]` is the deployment main.bicep calls registry-access.
    [GeneratedRegex(@"format\('\{0\}-(?<suffix>[a-z-]+)'")]
    private static partial Regex DeploymentNamePattern();
}
