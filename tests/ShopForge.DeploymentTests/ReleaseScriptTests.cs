using System.Text.Json.Nodes;

namespace ShopForge.DeploymentTests;

// What a release does and in which order, short of Azure itself: the workflow's steps, and the scripts it runs with a
// stand-in for the Azure CLI that answers as told and writes down what it was asked (D-205).
public sealed class ReleaseScriptTests : IDisposable
{
    private static readonly Dictionary<string, string?> Inputs = new()
    {
        ["NAME_PREFIX"] = "shopforge-test",
        ["LOCATION"] = "westeurope",
        ["REGISTRY_NAME"] = "shopforgetest",
        ["REGISTRY_SERVER"] = "shopforgetest.azurecr.io",
        ["REGISTRY_RESOURCE_GROUP"] = "shopforge-shared-rg",
        ["STORAGE_ACCOUNT"] = "shopforgetestmedia",
        ["STOREFRONT_FQDN"] = "shopforgetestmedia.z6.web.core.windows.net",
        ["STORE_HOST_SUFFIX"] = "shops.example.cz",
        ["API_BASE_URL"] = "https://api.example.cz",
        ["SITE_URL"] = "https://www.example.cz/",
        ["ADMIN_URL"] = "https://www.example.cz/admin/orders",
        ["SMOKE_STORE_HOST"] = "shop.shops.example.cz",
        ["IMAGE"] = "shopforgetest.azurecr.io/shopforge-api:0123456",
        ["DATABASE_PASSWORD"] = "database-password-made-up",
        ["EMAIL_PROVIDER"] = "mailgun",
        ["EMAIL_SENDER_ADDRESS"] = "shop@mail.example.cz",
        ["MAILGUN_DOMAIN"] = "mail.example.cz",
        ["MAILGUN_API_KEY"] = "mailgun-key-made-up",
        ["MAILGUN_WEBHOOK_SIGNING_KEY"] = "signing-key-made-up",
    };

    private readonly DirectoryInfo _azure = Directory.CreateTempSubdirectory("shopforge-az-");

    public ReleaseScriptTests()
    {
        var cli = Path.Combine(_azure.FullName, "az");
        File.WriteAllText(cli, AzureCli, new System.Text.UTF8Encoding(false));

        // The scripts are bash and run where bash does.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose() => _azure.Delete(recursive: true);

    // Steps run in the order the file gives them, so the order of what they run is the order of a release.
    [Fact]
    public void The_deployment_migrates_before_it_releases_and_checks_after_it_publishes()
    {
        var workflow = File.ReadAllText(Repository.PathOf(".github/workflows/deploy.yml"));
        string[] release =
        [
            "run: scripts/release-inputs.sh",
            "docker build",
            "run: scripts/provision.sh without-app",
            "run: scripts/migrate.sh",
            "run: scripts/provision.sh with-app",
            "scripts/smoke.sh --api",
            "az storage blob upload-batch",
            "scripts/smoke.sh --site",
        ];

        var positions = release.Select(step => workflow.IndexOf(step, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Equal(1, Occurrences(workflow, "provision.sh with-app"));
        Assert.DoesNotContain("az deployment", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("containerapp update", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_release_with_everything_it_needs_goes_ahead()
    {
        var checkedInputs = await RunAsync("scripts/release-inputs.sh", []);

        Assert.True(checkedInputs.ExitCode == 0, checkedInputs.Output);
    }

    [Theory]
    [InlineData("log")]
    [InlineData("LOG")]
    [InlineData("")]
    public async Task A_release_that_would_send_no_mail_stops_before_anything_is_built(string provider)
    {
        var checkedInputs = await RunAsync("scripts/release-inputs.sh", [], ("EMAIL_PROVIDER", provider));

        Assert.Equal(1, checkedInputs.ExitCode);
        Assert.Contains("EMAIL_PROVIDER", checkedInputs.Output, StringComparison.Ordinal);
    }

    // Missing inputs are named, never their values.
    [Fact]
    public async Task A_missing_input_is_named_and_no_secret_is_printed()
    {
        var checkedInputs = await RunAsync("scripts/release-inputs.sh", [], ("MAILGUN_WEBHOOK_SIGNING_KEY", ""), ("STORAGE_ACCOUNT", null));

        Assert.Equal(1, checkedInputs.ExitCode);
        Assert.Contains("The release is missing: STORAGE_ACCOUNT MAILGUN_WEBHOOK_SIGNING_KEY.", checkedInputs.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Inputs["MAILGUN_API_KEY"]!, checkedInputs.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("without-app", false)]
    [InlineData("with-app", true)]
    public async Task Each_deployment_of_a_release_says_whether_it_touches_the_app(string which, bool deploysApp)
    {
        var provisioned = await RunAsync("scripts/provision.sh", [which]);
        var parameters = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_azure.FullName, "parameters.json"), TestContext.Current.CancellationToken))!["parameters"]!;
        var asked = await AskedAsync();

        Assert.True(provisioned.ExitCode == 0, provisioned.Output);
        Assert.Equal(deploysApp, (bool)parameters["deployApp"]!["value"]!);
        Assert.Equal(Inputs["IMAGE"], (string?)parameters["image"]!["value"]);
        Assert.Equal(Inputs["MAILGUN_WEBHOOK_SIGNING_KEY"], (string?)parameters["mailgunWebhookSigningKey"]!["value"]);
        Assert.Contains(asked, line => line.StartsWith(
            $"deployment sub create --name shopforge-test-{which} --location westeurope --template-file infra/main.bicep --parameters @",
            StringComparison.Ordinal));
    }

    // The CLI is given the secrets in a file it alone can read, which is gone once the deployment is.
    [Fact]
    public async Task A_deployment_s_secrets_are_never_on_a_command_line_and_not_left_behind()
    {
        var provisioned = await RunAsync("scripts/provision.sh", ["with-app"]);
        var asked = string.Join('\n', await AskedAsync());
        var file = (await File.ReadAllTextAsync(Path.Combine(_azure.FullName, "parameters-path"), TestContext.Current.CancellationToken)).Trim();
        var mode = (await File.ReadAllTextAsync(Path.Combine(_azure.FullName, "parameters-mode"), TestContext.Current.CancellationToken)).Trim();

        Assert.True(provisioned.ExitCode == 0, provisioned.Output);
        Assert.All(
            new[] { "DATABASE_PASSWORD", "MAILGUN_API_KEY", "MAILGUN_WEBHOOK_SIGNING_KEY" },
            secret => Assert.DoesNotContain(Inputs[secret]!, asked, StringComparison.Ordinal));
        Assert.Equal("600", mode);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task A_migration_that_succeeds_lets_the_release_go_on()
    {
        await StatusesAsync("Running", "Running", "Succeeded");

        var migrated = await RunAsync("scripts/migrate.sh", ["shopforge-test-rg", "shopforge-test-migrate"]);

        Assert.True(migrated.ExitCode == 0, migrated.Output);
        Assert.Single(await AskedAsync(), line => line.StartsWith("containerapp job start", StringComparison.Ordinal));
        Assert.Contains("Migration execution-1 succeeded.", migrated.Output, StringComparison.Ordinal);
    }

    // A first deployment's job can start before its new roles have reached the vault, so a failed run is tried again.
    [Fact]
    public async Task A_migration_that_fails_once_is_tried_again()
    {
        await StatusesAsync("Running", "Failed", "Succeeded");

        var migrated = await RunAsync("scripts/migrate.sh", ["shopforge-test-rg", "shopforge-test-migrate"]);

        Assert.True(migrated.ExitCode == 0, migrated.Output);
        Assert.Equal(2, (await AskedAsync()).Count(line => line.StartsWith("containerapp job start", StringComparison.Ordinal)));
        Assert.Contains("Migration execution-2 succeeded.", migrated.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_migration_that_keeps_failing_stops_the_release()
    {
        await StatusesAsync("Failed", "Failed", "Failed");

        var migrated = await RunAsync("scripts/migrate.sh", ["shopforge-test-rg", "shopforge-test-migrate"]);

        Assert.Equal(1, migrated.ExitCode);
        Assert.Equal(3, (await AskedAsync()).Count(line => line.StartsWith("containerapp job start", StringComparison.Ordinal)));
        Assert.Contains("left on the image it was serving", migrated.Output, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private Task StatusesAsync(params string[] statuses) =>
        File.WriteAllLinesAsync(Path.Combine(_azure.FullName, "statuses"), statuses, TestContext.Current.CancellationToken);

    private async Task<List<string>> AskedAsync()
    {
        var log = Path.Combine(_azure.FullName, "asked");

        return File.Exists(log) ? [.. await File.ReadAllLinesAsync(log, TestContext.Current.CancellationToken)] : [];
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string script, string[] arguments, params (string Name, string? Value)[] changed)
    {
        var environment = new Dictionary<string, string?>(Inputs)
        {
            ["PATH"] = $"{_azure.FullName}:{Environment.GetEnvironmentVariable("PATH")}",
            ["AZ_STATE"] = _azure.FullName,
            ["MIGRATION_RETRY_PAUSE_SECONDS"] = "0",
            ["MIGRATION_POLL_SECONDS"] = "0",
        };

        foreach (var (name, value) in changed)
        {
            environment[name] = value;
        }

        var run = await Repository.RunAsync("bash", [script, .. arguments], environment);

        return (run.ExitCode, run.Output + run.Errors);
    }

    // Answers what the scripts ask the way the Azure CLI does, from what the test has told it, and keeps the parameters
    // file it was handed before the script that made it removes it.
    private const string AzureCli = """
        #!/usr/bin/env bash
        echo "$*" >> "$AZ_STATE/asked"
        case "$1 $2 $3" in
          "containerapp job start")
            count=$(( $(cat "$AZ_STATE/starts" 2>/dev/null || echo 0) + 1 ))
            echo "$count" > "$AZ_STATE/starts"
            echo "execution-$count"
            ;;
          "containerapp job execution")
            status=$(head -n 1 "$AZ_STATE/statuses")
            echo "${status:-Failed}"
            tail -n +2 "$AZ_STATE/statuses" > "$AZ_STATE/statuses.next" && mv "$AZ_STATE/statuses.next" "$AZ_STATE/statuses"
            ;;
          "deployment sub create")
            for argument in "$@"; do
              case "$argument" in
                @*)
                  cp "${argument#@}" "$AZ_STATE/parameters.json"
                  echo "${argument#@}" > "$AZ_STATE/parameters-path"
                  stat -c '%a' "${argument#@}" > "$AZ_STATE/parameters-mode"
                  ;;
              esac
            done
            ;;
        esac
        """;
}
