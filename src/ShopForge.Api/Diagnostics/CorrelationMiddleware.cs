using System.Diagnostics;

namespace ShopForge.Api.Diagnostics;

// One id ties a screenshot, a log line and a trace together (D-072). The store is added by the store resolution
// middleware, so a store's lines can be read on their own.
internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();

        if (traceId is null)
        {
            await next(context);
            return;
        }

        context.Response.Headers[HeaderName] = traceId;

        using (logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId }))
        {
            await next(context);
        }
    }
}
