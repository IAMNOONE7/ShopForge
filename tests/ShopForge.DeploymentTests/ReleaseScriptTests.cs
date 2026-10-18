using System.Text.Json.Nodes;

namespace ShopForge.DeploymentTests;

// What a release does and in which order, short of Azure itself: the workflow's steps, and the scripts it runs with a
// stand-in for the Azure CLI that answers as told and writes down what it was asked. The stand-in compiles the
// environment's parameter file with the real compiler, so a release whose inputs would not compile fails here
// (D-205, D-208).
public sealed class ReleaseScriptTests : IDisposable
{
    private static readonly Dictionary<string, string?> Inputs = new()
    {
        ["ENVIRONMENT"] = "staging",
        ["LOCATION"] = "westeurope",
        ["REGISTRY_NAME"] = "shopforgeshared",
        ["REGISTRY_SERVER"] = "shopforgeshared.azurecr.io",
        ["REGISTRY_RESOURCE_GROUP"] = "shopforge-shared-rg",
        ["STORE_HOST_SUFFIX"] = "shops.example.cz",
        ["ADMIN_HOST"] = "admin.example.cz",
        ["FIRST_OPERATOR_EMAIL"] = "ops@example.cz",
        ["IMAGE"] = "shopforgeshared.azurecr.io/shopforge-api:0123456",
        ["DATABASE_PASSWORD"] = "database-password-made-up",
        ["EMAIL_PROVIDER"] = "mailgun",
        ["EMAIL_SENDER_ADDRESS"] = "shop@mail.example.cz",
        ["MAILGUN_DOMAIN"] = "mail.example.cz",
        ["MAILGUN_API_KEY"] = "mailgun-key-made-up",
        ["MAILGUN_WEBHOOK_SIGNING_KEY"] = "signing-key-made-up",
    };

    // What the stand-in says production's deployments made. Production's names start shopforge-prod, not with the
    // environment's name, so a script that guessed a name instead of asking would be found out.
    private const string Outputs = """
        {
          "resourceGroup": { "type": "String", "value": "shopforge-prod-rg" },
          "migrationJob": { "type": "String", "value": "shopforge-prod-migrate" },
          "publicHostName": { "type": "String", "value": "shopforge-prod-a1b2.z01.azurefd.net" },
          "apiFqdn": { "type": "String", "value": "shopforge-prod-api.westeurope.azurecontainerapps.io" }
        }
        """;

    private readonly DirectoryInfo _stand = Directory.CreateTempSubdirectory("shopforge-az-");

    public ReleaseScriptTests()
    {
        Write("az", AzureCli);
        Write("smoke.sh", "#!/usr/bin/env bash\necho \"$*\" >> \"$AZ_STATE/smoked\"\n");
        File.WriteAllText(Path.Combine(_stand.FullName, "outputs.json"), Outputs);
    }

    public void Dispose() => _stand.Delete(recursive: true);

    // Steps run in the order the file gives them, so the order of what they run is the order of a release.
    [Fact]
    public void The_deployment_migrates_before_it_releases_and_checks_after_it_publishes()
    {
        var workflow = File.ReadAllText(Repository.PathOf(".github/workflows/deploy.yml"));
        string[] release =
        [
            "run: scripts/release-inputs.sh",
            "docker build",
            "run: scripts/provision.sh \"$ENVIRONMENT\" without-app",
            "run: scripts/migrate.sh \"$ENVIRONMENT\"",
            "run: scripts/provision.sh \"$ENVIRONMENT\" with-app",
            "run: scripts/release-smoke.sh \"$ENVIRONMENT\" \"${{ github.sha }}\" api",
            "run: scripts/release-smoke.sh \"$ENVIRONMENT\" \"${{ github.sha }}\" site",
        ];

        var positions = release.Select(step => workflow.IndexOf(step, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Equal(1, Occurrences(workflow, "with-app"));
        Assert.Contains("ENVIRONMENT: ${{ inputs.environment }}", workflow, StringComparison.Ordinal);
        Assert.Contains("FIRST_OPERATOR_EMAIL: ${{ vars.FIRST_OPERATOR_EMAIL }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("az deployment", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("containerapp update", workflow, StringComparison.Ordinal);

        // The frontends ship in the image, so nothing is published beside it.
        Assert.DoesNotContain("upload-batch", workflow, StringComparison.Ordinal);
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
        var checkedInputs = await RunAsync("scripts/release-inputs.sh", [], ("MAILGUN_WEBHOOK_SIGNING_KEY", ""), ("REGISTRY_RESOURCE_GROUP", null));

        Assert.Equal(1, checkedInputs.ExitCode);
        Assert.Contains("The release is missing: REGISTRY_RESOURCE_GROUP MAILGUN_WEBHOOK_SIGNING_KEY.", checkedInputs.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Inputs["MAILGUN_API_KEY"]!, checkedInputs.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_environment_without_a_parameter_file_is_no_environment()
    {
        var checkedInputs = await RunAsync("scripts/release-inputs.sh", [], ("ENVIRONMENT", "qa"));

        Assert.Equal(1, checkedInputs.ExitCode);
        Assert.Contains("There is no environment called 'qa'", checkedInputs.Output, StringComparison.Ordinal);
    }

    // The CLI is handed the environment's own file and every input in its environment, and compiles one from the other.
    [Theory]
    [InlineData("staging", "without-app", false, "shopforge-staging")]
    [InlineData("production", "with-app", true, "shopforge-prod")]
    public async Task Each_deployment_compiles_its_environment_s_file_from_the_release_s_inputs(
        string environment, string which, bool deploysApp, string namePrefix)
    {
        var provisioned = await RunAsync("scripts/provision.sh", [environment, which]);

        Assert.True(provisioned.ExitCode == 0, provisioned.Output);

        var parameters = await CompiledParametersAsync();

        Assert.Contains(
            $"deployment sub create --name shopforge-{environment}-{which} --location westeurope --parameters infra/environments/{environment}.bicepparam",
            await AskedAsync());
        Assert.Equal(namePrefix, (string?)parameters["namePrefix"]!["value"]);
        Assert.Equal(deploysApp, (bool)parameters["deployApp"]!["value"]!);
        Assert.Equal(Inputs["IMAGE"], (string?)parameters["image"]!["value"]);
        Assert.Equal(Inputs["ADMIN_HOST"], (string?)parameters["adminHost"]!["value"]);
        Assert.Equal(Inputs["FIRST_OPERATOR_EMAIL"], (string?)parameters["firstOperatorEmail"]!["value"]);
        Assert.Equal(Inputs["REGISTRY_RESOURCE_GROUP"], (string?)parameters["registryResourceGroup"]!["value"]);
        Assert.Equal(Inputs["EMAIL_SENDER_ADDRESS"], (string?)parameters["emailSenderAddress"]!["value"]);
        Assert.Equal(Inputs["DATABASE_PASSWORD"], (string?)parameters["databasePassword"]!["value"]);
        Assert.Equal(Inputs["MAILGUN_WEBHOOK_SIGNING_KEY"], (string?)parameters["mailgunWebhookSigningKey"]!["value"]);
    }

    [Fact]
    public async Task A_deployment_s_secrets_are_never_on_a_command_line()
    {
        var provisioned = await RunAsync("scripts/provision.sh", ["staging", "with-app"]);
        var asked = string.Join('\n', await AskedAsync());

        Assert.True(provisioned.ExitCode == 0, provisioned.Output);
        Assert.All(
            new[] { "DATABASE_PASSWORD", "MAILGUN_API_KEY", "MAILGUN_WEBHOOK_SIGNING_KEY" },
            secret => Assert.DoesNotContain(Inputs[secret]!, asked, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("")]
    public async Task There_is_no_deploying_an_environment_that_does_not_exist(string environment)
    {
        var provisioned = await RunAsync("scripts/provision.sh", [environment, "with-app"]);

        Assert.Equal(2, provisioned.ExitCode);
        Assert.Empty(await AskedAsync());
    }

    [Fact]
    public async Task A_migration_runs_the_job_the_first_deployment_made()
    {
        await StatusesAsync("Running", "Succeeded");

        var migrated = await RunAsync("scripts/migrate.sh", ["production"]);
        var asked = await AskedAsync();

        Assert.True(migrated.ExitCode == 0, migrated.Output);
        Assert.Contains("deployment sub show --name shopforge-production-without-app --query properties.outputs --output json", asked);
        Assert.Single(asked, line => line.StartsWith("containerapp job start --resource-group shopforge-prod-rg --name shopforge-prod-migrate ", StringComparison.Ordinal));
        Assert.Contains("Migration execution-1 succeeded.", migrated.Output, StringComparison.Ordinal);
    }

    // A first deployment's job can start before its new roles have reached the vault, so a failed run is tried again.
    [Fact]
    public async Task A_migration_that_fails_once_is_tried_again()
    {
        await StatusesAsync("Running", "Failed", "Succeeded");

        var migrated = await RunAsync("scripts/migrate.sh", ["production"]);

        Assert.True(migrated.ExitCode == 0, migrated.Output);
        Assert.Equal(2, (await AskedAsync()).Count(line => line.StartsWith("containerapp job start", StringComparison.Ordinal)));
        Assert.Contains("Migration execution-2 succeeded.", migrated.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_migration_that_keeps_failing_stops_the_release()
    {
        await StatusesAsync("Failed", "Failed", "Failed");

        var migrated = await RunAsync("scripts/migrate.sh", ["production"]);

        Assert.Equal(1, migrated.ExitCode);
        Assert.Equal(3, (await AskedAsync()).Count(line => line.StartsWith("containerapp job start", StringComparison.Ordinal)));
        Assert.Contains("left on the image it was serving", migrated.Output, StringComparison.Ordinal);
    }

    // Every address the smoke tests use is one the release's own deployment reported, so a new environment with no shop
    // and no domain of its own is tested all the same. The admin is checked on the edge's own host name, where it is
    // served whatever its own domain's DNS is doing.
    [Fact]
    public async Task The_api_is_smoke_tested_through_the_edge_the_release_made()
    {
        var smoked = await RunAsync("scripts/release-smoke.sh", ["production", "0123456789abcdef", "api"]);

        Assert.True(smoked.ExitCode == 0, smoked.Output);
        Assert.Contains("deployment sub show --name shopforge-production-with-app --query properties.outputs --output json", await AskedAsync());
        Assert.Equal(["--api https://shopforge-prod-a1b2.z01.azurefd.net --version 0123456789abcdef"], await SmokedAsync());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("shop.shops.example.cz", " --store https://shop.shops.example.cz/")]
    public async Task The_site_is_smoke_tested_with_a_shop_only_once_one_is_named(string? store, string storeArguments)
    {
        var smoked = await RunAsync("scripts/release-smoke.sh", ["production", "0123456789abcdef", "site"], ("SMOKE_STORE_HOST", store));

        Assert.True(smoked.ExitCode == 0, smoked.Output);
        Assert.Equal(
            [
                "--site https://shopforge-prod-a1b2.z01.azurefd.net/ --admin https://shopforge-prod-a1b2.z01.azurefd.net/admin/orders "
                + "--http http://shopforge-prod-a1b2.z01.azurefd.net/ --origin https://shopforge-prod-api.westeurope.azurecontainerapps.io"
                + storeArguments,
            ],
            await SmokedAsync());
    }

    private static int Occurrences(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private Task StatusesAsync(params string[] statuses) =>
        File.WriteAllLinesAsync(Path.Combine(_stand.FullName, "statuses"), statuses, TestContext.Current.CancellationToken);

    private Task<List<string>> AskedAsync() => LinesAsync("asked");

    private Task<List<string>> SmokedAsync() => LinesAsync("smoked");

    private async Task<JsonNode> CompiledParametersAsync()
    {
        var compiled = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_stand.FullName, "compiled.json"), TestContext.Current.CancellationToken))!;

        return JsonNode.Parse((string)compiled["parametersJson"]!)!["parameters"]!;
    }

    private async Task<List<string>> LinesAsync(string name)
    {
        var path = Path.Combine(_stand.FullName, name);

        return File.Exists(path) ? [.. await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken)] : [];
    }

    private void Write(string name, string script)
    {
        var path = Path.Combine(_stand.FullName, name);
        File.WriteAllText(path, script, new System.Text.UTF8Encoding(false));

        // The scripts are bash and run where bash does.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string script, string[] arguments, params (string Name, string? Value)[] changed)
    {
        var environment = new Dictionary<string, string?>(Inputs)
        {
            ["PATH"] = $"{_stand.FullName}:{Environment.GetEnvironmentVariable("PATH")}",
            ["AZ_STATE"] = _stand.FullName,
            ["BICEP"] = Repository.BicepPath,
            ["SMOKE_SCRIPT"] = Path.Combine(_stand.FullName, "smoke.sh"),
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

    // Answers what the scripts ask the way the Azure CLI does, from what the test has told it. A deployment's parameter
    // file is compiled in the environment the CLI was given, as the CLI compiles it.
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
          "deployment sub show")
            cat "$AZ_STATE/outputs.json"
            ;;
          "deployment sub create")
            while [ "$#" -gt 0 ]; do
              if [ "$1" = --parameters ]; then
                "$BICEP" build-params "$2" --stdout > "$AZ_STATE/compiled.json" || exit 1
              fi
              shift
            done
            ;;
        esac
        """;
}
