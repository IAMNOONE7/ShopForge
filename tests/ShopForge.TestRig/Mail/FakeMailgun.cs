using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using ShopForge.TestRig.Runs;

namespace ShopForge.TestRig.Mail;

// Mailgun's sending call as ShopForge makes it — a multipart form to /v3/{domain}/messages with Basic "api:<key>" —
// keeping every message instead of sending it. A run reads only mail to addresses at its own domain, which is how two
// runs on one stack stay out of each other's post (D-203).
internal sealed partial class FakeMailgun
{
    private readonly ConcurrentQueue<Letter> _letters = new();

    public Letter Keep(string to, string from, string subject, string text, string? html, IReadOnlyList<string> attachments)
    {
        // A link that ends a sentence is followed by its full stop, which is the sentence's and not the link's.
        var links = Links().Matches(text).Select(link => link.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')')).ToList();
        var letter = new Letter(Guid.NewGuid(), to, from, subject, text, html, attachments, DateTimeOffset.UtcNow, links);
        _letters.Enqueue(letter);

        return letter;
    }

    public IEnumerable<Letter> To(string domain, string? address) =>
        _letters.Where(letter =>
            letter.To.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase)
            && (address is null || string.Equals(letter.To, address, StringComparison.OrdinalIgnoreCase)));

    [GeneratedRegex(@"https?://[^\s<>""]+")]
    private static partial Regex Links();
}

internal sealed record Letter(
    Guid Id,
    string To,
    string From,
    string Subject,
    string Text,
    string? Html,
    IReadOnlyList<string> Attachments,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<string> Links);

internal static class MailgunEndpoints
{
    public static void MapFakeMailgun(this WebApplication app)
    {
        app.MapPost("/mailgun/v3/{domain}/messages", KeepAsync);
        app.MapGet("/runs/{runId}/mail", Read);
    }

    private static async Task<IResult> KeepAsync(string domain, HttpRequest request, FakeMailgun mailgun)
    {
        if (!request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.Ordinal) || !request.HasFormContentType)
        {
            return Results.Unauthorized();
        }

        var form = await request.ReadFormAsync();
        var recipients = form["to"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var attachments = form.Files.Select(file => file.FileName).ToList();

        foreach (var recipient in recipients)
        {
            mailgun.Keep(recipient, form["from"].ToString(), form["subject"].ToString(), form["text"].ToString(), form["html"].ToString() is { Length: > 0 } html ? html : null, attachments);
        }

        return Results.Json(new { id = $"<{Guid.NewGuid():N}@{domain}>", message = "Queued. Thank you." });
    }

    private static Results<Ok<List<Letter>>, NotFound> Read(string runId, string? to, RunBook runs, FakeMailgun mailgun) =>
        runs.Find(runId) is { } run
            ? TypedResults.Ok(mailgun.To(run.EmailDomain, to).ToList())
            : TypedResults.NotFound();
}
