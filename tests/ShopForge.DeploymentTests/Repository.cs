using System.Diagnostics;
using System.Reflection;

namespace ShopForge.DeploymentTests;

// The repository these tests were built from, and the tools they run against it.
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(string relative) => Path.Combine(Root, relative);

    public static string BicepPath { get; } = typeof(Repository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "BicepPath").Value
        ?? throw new InvalidOperationException("The build did not say where the Bicep compiler is.");

    public static async Task<(int ExitCode, string Output, string Errors)> RunAsync(
        string fileName, IEnumerable<string> arguments, IDictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Root,
        };

        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);

        // A script that never ends fails its test rather than the whole run.
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return (-1, "", $"{fileName} did not finish within two minutes.");
        }

        return (process.ExitCode, await output, await errors);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShopForge.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from inside the repository they test.");
    }
}
