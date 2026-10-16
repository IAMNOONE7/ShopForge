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
                "param storefrontFqdn = 'site.example.cz'",
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

    [GeneratedRegex(@"'Microsoft\.Resources/deployments', (?<name>'[^']+'|format\([^)]*\)[^)]*\))\)\]$")]
    private static partial Regex DeploymentReference();

    // `[format('{0}-registry-access', parameters('namePrefix'))]` is the deployment main.bicep calls registry-access.
    [GeneratedRegex(@"format\('\{0\}-(?<suffix>[a-z-]+)'")]
    private static partial Regex DeploymentNamePattern();
}
