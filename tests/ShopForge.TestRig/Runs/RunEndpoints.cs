using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ShopForge.TestRig.Runs;

internal static class RunEndpoints
{
    public static void MapRuns(this WebApplication app)
    {
        app.MapPost("/runs", StartAsync);
        app.MapGet("/runs/{runId}", Describe);
        app.MapDelete("/runs/{runId}", EndAsync);
        app.MapPost("/runs/{runId}/passes/{name}", RunPassAsync);
        app.MapPost("/runs/{runId}/clock", AdvanceAsync);
    }

    // A run that cannot be made says which step refused: a 502, because the failure is the API's answer, not the rig's.
    private static async Task<Results<Created<Run>, ProblemHttpResult>> StartAsync(RunSetup setup, CancellationToken cancellationToken)
    {
        try
        {
            var run = await setup.StartAsync(cancellationToken);

            return TypedResults.Created($"/runs/{run.Id}", run);
        }
        catch (RunSetupException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The run could not be set up", detail: exception.Message);
        }
    }

    private static Results<Ok<Run>, NotFound> Describe(string runId, RunBook runs) =>
        runs.Find(runId) is { } run ? TypedResults.Ok(run) : TypedResults.NotFound();

    // The run's company is suspended, so its stores stop answering, and the rig forgets it. The database goes with the
    // stack; this is for a stack that runs many journeys before it is thrown away.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> EndAsync(
        string runId, RunBook runs, RigOptions options, CancellationToken cancellationToken)
    {
        if (runs.Find(runId) is not { } run)
        {
            return TypedResults.NotFound();
        }

        try
        {
            using var platform = new ApiSession(options.ApiOrigin);
            await platform.PostAsync("api/platform/auth/login", new { email = options.OperatorEmail, password = options.OperatorPassword }, HttpStatusCode.OK, cancellationToken);
            await platform.PostAsync($"api/platform/tenants/{run.TenantId}/suspend", null, HttpStatusCode.OK, cancellationToken);
        }
        catch (RunSetupException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The run could not be ended", detail: exception.Message);
        }

        runs.Forget(runId);

        return TypedResults.NoContent();
    }

    // The stack's clock moved on. It is the whole stack's, so a journey that moves it moves it for every run on the stack:
    // runs that move time belong on a stack of their own, or one after another.
    private static async Task<Results<Ok<System.Text.Json.JsonElement>, NotFound, ProblemHttpResult>> AdvanceAsync(
        string runId, ClockMove move, RunBook runs, RigOptions options, CancellationToken cancellationToken)
    {
        if (runs.Find(runId) is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            using var anyone = new ApiSession(options.ApiOrigin);

            return TypedResults.Ok(await anyone.PostAsync("api/e2e/clock", new { advanceMinutes = move.AdvanceMinutes }, HttpStatusCode.OK, cancellationToken));
        }
        catch (RunSetupException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The clock did not move", detail: exception.Message);
        }
    }

    // A scheduled pass run now — the expiry sweep, the catch-up — so a journey that waits for one does not wait for its
    // period. The passes walk every store, which on a stack of runs is only ever runs' stores.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RunPassAsync(
        string runId, string name, RunBook runs, RigOptions options, CancellationToken cancellationToken)
    {
        if (runs.Find(runId) is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            using var anyone = new ApiSession(options.ApiOrigin);
            await anyone.PostAsync($"api/e2e/passes/{Uri.EscapeDataString(name)}", null, HttpStatusCode.NoContent, cancellationToken);
        }
        catch (RunSetupException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The pass did not run", detail: exception.Message);
        }

        return TypedResults.NoContent();
    }
}

internal sealed record ClockMove(double AdvanceMinutes);
