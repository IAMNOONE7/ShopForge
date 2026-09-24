using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ShopForge.Api.Errors;

// A row that another request has already changed or removed — a double-clicked checkout is the everyday case. The
// work is rolled back either way; what differs is whether the caller is told they were second or shown a fault.
internal sealed class ConcurrentChangeExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Something else changed this at the same time",
                Detail = "The request was not applied. Reload what you were looking at and try again.",
            },
        });
    }
}
