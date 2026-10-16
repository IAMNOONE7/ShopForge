using ShopForge.Infrastructure.Persistence;

namespace ShopForge.Api.Health;

// Readiness keeps a revision whose schema is behind out of rotation; this is for anybody who reaches it anyway. Until
// the database has caught up with the build, everything but its health is answered 503, so new code is never served
// against an old schema, whichever order a release ran in (D-205).
internal static class SchemaGateMiddleware
{
    private const string RetryAfterSeconds = "5";

    public static IApplicationBuilder UseSchemaGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var schema = context.RequestServices.GetRequiredService<SchemaGate>();

            if (schema.IsCaughtUp || context.Request.Path.StartsWithSegments("/health"))
            {
                await next(context);

                return;
            }

            string title;

            try
            {
                if ((await schema.PendingAsync(context.RequestAborted)).Count == 0)
                {
                    await next(context);

                    return;
                }

                title = "This build is waiting for its database to be migrated.";
            }
            catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
            {
                title = "This build cannot reach its database yet.";
            }

            context.Response.Headers.RetryAfter = RetryAfterSeconds;
            await TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: title).ExecuteAsync(context);
        });
}
