using Microsoft.AspNetCore.Diagnostics;

namespace ShopForge.Api.Errors;

// A body the API cannot read is the caller's mistake, not a server fault — and in Development the framework
// throws over it instead of answering itself, so without this the same request would answer 400 or 500
// depending on where it runs.
internal sealed class MalformedRequestExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = badRequest.StatusCode, Title = "The request could not be read" },
        });
    }
}
