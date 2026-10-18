using System.Diagnostics;

namespace ShopForge.IntegrationTests;

// The built API run as a process with a command, the way a deployment's job runs it, against a database of the test's
// own.
internal static class ApiCommand
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        ShopForgeApiFactory factory, OwnDatabase database, string[] arguments, IDictionary<string, string>? settings = null)
    {
        var start = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "ShopForge.Api.dll"), .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__ShopForge"] = database.ConnectionString;
        start.Environment["ConnectionStrings__FileStorage"] = factory.FileStorageConnectionString;

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        using var process = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);

        // A build that serves instead of exiting fails here rather than hanging the suite.
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return (-1, "The build did not exit within two minutes.");
        }

        return (process.ExitCode, await output + await errors);
    }
}
