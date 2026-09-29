using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.Shared.Http;

// A shopper whose connection drops between sending a checkout and seeing the answer has no way to know whether the
// order was placed, so they press the button again. The cart row happened to stop the second one; nothing else
// did, and nothing at all does for a return or a refund. A key the caller chooses makes the retry explicit: the
// first request does the work, and every repeat of it is answered with what the first one said (D-131).
public static class Idempotency
{
    public const string HeaderName = "Idempotency-Key";

    public const int MaxKeyLength = 128;

    // The stored answer is replayed byte for byte, so the caller cannot tell a retry from the original except by
    // this header — which is there for somebody reading a trace, not for the caller to branch on.
    public const string ReplayHeaderName = "Idempotent-Replay";

    public static TBuilder Idempotent<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new IdempotencyFilter());
}

// Where a request that carried a key is kept. The key is claimed before the work starts and the answer is written
// after it commits, so two identical requests in flight together cannot both be doing the work.
public interface IIdempotentRequests
{
    Task<IdempotencyClaim> ClaimAsync(string endpoint, string key, string fingerprint, CancellationToken cancellationToken);

    Task CompleteAsync(string endpoint, string key, IdempotentResponse response, CancellationToken cancellationToken);

    // A request that failed leaves no answer to replay, so the key is given back and the caller may try it again.
    Task ReleaseAsync(string endpoint, string key, CancellationToken cancellationToken);
}

public enum IdempotencyVerdict
{
    Claimed,
    Replay,
    InFlight,
    Mismatch,
}

public sealed record IdempotencyClaim(IdempotencyVerdict Verdict, IdempotentResponse? Response = null);

public sealed record IdempotentResponse(int StatusCode, string? ContentType, string? Location, string Body);

internal sealed class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (httpContext.Request.Headers[Idempotency.HeaderName].ToString() is not { Length: > 0 } key)
        {
            return await next(context);
        }

        if (key.Length > Idempotency.MaxKeyLength)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The idempotency key is too long",
                detail: $"A key may be up to {Idempotency.MaxKeyLength} characters.");
        }

        var requests = httpContext.RequestServices.GetService<IIdempotentRequests>();

        if (requests is null)
        {
            return await next(context);
        }

        var endpoint = $"{httpContext.Request.Method} {(httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText}";
        var claim = await requests.ClaimAsync(endpoint, key, await FingerprintAsync(httpContext), httpContext.RequestAborted);

        switch (claim.Verdict)
        {
            case IdempotencyVerdict.Replay:
                return new StoredResponse(claim.Response!, replayed: true);

            // The first request is still running. Answering 409 rather than waiting for it keeps one slow request
            // from becoming two, and the caller already knows to try again.
            case IdempotencyVerdict.InFlight:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A request with this key is still in progress",
                    detail: "Wait for the first one to answer before sending it again.");

            case IdempotencyVerdict.Mismatch:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "This idempotency key was used for a different request",
                    detail: "A key belongs to one request. Use a new one.");

            default:
                break;
        }

        object? result;

        try
        {
            result = await next(context);
        }
        catch
        {
            await requests.ReleaseAsync(endpoint, key, CancellationToken.None);

            throw;
        }

        // Only a handler that answers with a result can have its answer kept; anything else gives the key back
        // rather than leaving it claimed forever.
        if (result is not IResult produced)
        {
            await requests.ReleaseAsync(endpoint, key, CancellationToken.None);

            return result;
        }

        var response = await CaptureAsync(httpContext, produced);

        // Only an answer that did something is worth keeping. A request that was refused changed nothing, so the
        // key goes back: a shopper who fixes their cart and presses the button again gets a real attempt rather
        // than yesterday's refusal read back to them.
        if (response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            await requests.ReleaseAsync(endpoint, key, CancellationToken.None);
        }
        else
        {
            await requests.CompleteAsync(endpoint, key, response, CancellationToken.None);
        }

        return new StoredResponse(response, replayed: false);
    }

    // What the caller sent, so that the same key on a different request is caught rather than answered with
    // somebody else's order. The body is readable twice because the request carried a key (see Program.cs).
    private static async Task<string> FingerprintAsync(HttpContext httpContext)
    {
        var body = httpContext.Request.Body;

        if (!body.CanSeek)
        {
            return string.Empty;
        }

        body.Position = 0;
        var hash = await SHA256.HashDataAsync(body, httpContext.RequestAborted);
        body.Position = 0;

        return Convert.ToHexStringLower(hash);
    }

    // The answer is written into a buffer rather than straight onto the wire, because it has to be kept before it
    // is sent: a response the caller received but the store did not record would be done twice on the retry.
    private static async Task<IdempotentResponse> CaptureAsync(HttpContext httpContext, IResult result)
    {
        var wire = httpContext.Response.Body;
        using var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;

        try
        {
            await result.ExecuteAsync(httpContext);
        }
        finally
        {
            httpContext.Response.Body = wire;
        }

        return new IdempotentResponse(
            httpContext.Response.StatusCode,
            httpContext.Response.ContentType,
            httpContext.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
            Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private sealed class StoredResponse(IdempotentResponse response, bool replayed) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = response.StatusCode;

            if (response.ContentType is { Length: > 0 } contentType)
            {
                httpContext.Response.ContentType = contentType;
            }

            if (response.Location is { Length: > 0 } location)
            {
                httpContext.Response.Headers.Location = location;
            }

            if (replayed)
            {
                httpContext.Response.Headers[Idempotency.ReplayHeaderName] = "true";
            }

            await httpContext.Response.WriteAsync(response.Body, httpContext.RequestAborted);
        }
    }
}
