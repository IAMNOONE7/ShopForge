namespace ShopForge.UnitTests.Deployment;

// The container is the one place the application runs without a developer's machine under it. Alpine's default
// .NET image has no ICU and no time-zone data and runs invariant, where asking for "cs-CZ" throws: every store's
// culture refused, every order e-mail and every invoice failing, while every test here passed (D-197). Nothing
// builds the image before the first deployment, so this reads what it would be built from.
public sealed class ContainerImageTests
{
    private static readonly string[] Dockerfile = File.ReadAllLines(Path.Combine(RepositoryRoot(), "Dockerfile"));

    [Fact]
    public void The_runtime_image_carries_cultures_and_time_zones()
    {
        var runtime = Dockerfile.Last(line => line.StartsWith("FROM ", StringComparison.Ordinal));

        Assert.Matches(@"^FROM mcr\.microsoft\.com/dotnet/aspnet:\S+-extra\s", runtime);
        Assert.Contains("ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false", Dockerfile);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShopForge.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
