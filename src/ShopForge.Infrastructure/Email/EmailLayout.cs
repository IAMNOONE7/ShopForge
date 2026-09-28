using System.Net;
using System.Text;
using ShopForge.Shared.Stores;

namespace ShopForge.Infrastructure.Email;

// The same words the text part carries, wrapped in the store's colour and logo. Built here rather than in the
// modules that write the messages: a shop's mail should look like the shop without every handler knowing what a
// stylesheet is, and there is exactly one place to change when it should look better (D-121).
internal static class EmailLayout
{
    public static string Render(string title, string body, string senderName, StoreBranding? branding)
    {
        var colour = Colour(branding?.PrimaryColor);
        var html = new StringBuilder();

        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f4f5;")
            .Append("font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#18181b;\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;overflow:hidden;")
            .Append("border:1px solid #e4e4e7;\">")
            .Append("<div style=\"background:").Append(colour).Append(";padding:20px 24px;\">");

        if (branding?.LogoUrl is { Length: > 0 } logo)
        {
            html.Append("<img src=\"").Append(WebUtility.HtmlEncode(logo)).Append("\" alt=\"")
                .Append(WebUtility.HtmlEncode(senderName)).Append("\" style=\"max-height:40px;display:block;\">");
        }
        else
        {
            html.Append("<span style=\"color:#ffffff;font-size:18px;font-weight:600;\">")
                .Append(WebUtility.HtmlEncode(senderName)).Append("</span>");
        }

        html.Append("</div><div style=\"padding:24px;\">")
            .Append("<h1 style=\"margin:0 0 16px;font-size:20px;\">").Append(WebUtility.HtmlEncode(title)).Append("</h1>");

        foreach (var paragraph in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            html.Append("<p style=\"margin:0 0 12px;line-height:1.5;\">").Append(Paragraph(paragraph, colour)).Append("</p>");
        }

        return html.Append("</div><div style=\"padding:16px 24px;background:#fafafa;color:#71717a;font-size:12px;\">")
            .Append(WebUtility.HtmlEncode(senderName))
            .Append("</div></div></body></html>")
            .ToString();
    }

    // Everything is encoded first and only then are links put back, so nothing a customer or a store typed can
    // become markup of its own.
    private static string Paragraph(string paragraph, string colour) =>
        string.Join(' ', paragraph.Split(' ').Select(word => Link(word, colour)));

    private static string Link(string word, string colour)
    {
        var encoded = WebUtility.HtmlEncode(word);

        if (!word.StartsWith("http://", StringComparison.Ordinal) && !word.StartsWith("https://", StringComparison.Ordinal))
        {
            return encoded;
        }

        var trimmed = word.TrimEnd('.', ',');
        var tail = encoded[WebUtility.HtmlEncode(trimmed).Length..];

        return $"<a href=\"{WebUtility.HtmlEncode(trimmed)}\" style=\"color:{colour};\">{WebUtility.HtmlEncode(trimmed)}</a>{tail}";
    }

    // A colour comes from a store's theme and ends up inside a style attribute, so anything that is not plainly a
    // hex colour is not used at all.
    private static string Colour(string? primaryColor) =>
        primaryColor is { Length: 7 } value
            && value[0] == '#'
            && value[1..].All(Uri.IsHexDigit)
                ? value
                : "#18181b";
}
