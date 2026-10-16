using ShopForge.TestRig.Comgate;
using ShopForge.TestRig.Mail;
using ShopForge.TestRig.Oracle;
using ShopForge.TestRig.Runs;

namespace ShopForge.TestRig;

// What an end-to-end journey talks to besides ShopForge itself: Comgate and Mailgun as they are on the wire, a run of
// its own to work in, and a read-only look at what that run left in the database. It runs beside an API in the E2E
// environment and refuses to start anywhere else (D-203).
public sealed class Rig
{
    private Rig()
    {
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (!builder.Environment.IsEnvironment(RigOptions.EnvironmentName))
        {
            throw new InvalidOperationException($"The test rig runs only in the {RigOptions.EnvironmentName} environment.");
        }

        builder.Services.AddSingleton(RigOptions.From(builder.Configuration));
        builder.Services.AddSingleton<FakeComgate>();
        builder.Services.AddSingleton<FakeMailgun>();
        builder.Services.AddSingleton<RunBook>();
        builder.Services.AddSingleton<RunSetup>();
        builder.Services.AddSingleton<Readings>();
        builder.Services.AddHttpClient();

        var app = builder.Build();

        app.MapGet("/health", () => TypedResults.Ok("The rig is up."));
        app.MapFakeComgate();
        app.MapFakeMailgun();
        app.MapRuns();
        app.MapReadings();

        app.Run();
    }
}
