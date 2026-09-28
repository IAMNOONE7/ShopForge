using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Stores;

namespace ShopForge.UnitTests.Email;

public sealed class EmailLayoutTests
{
    [Fact]
    public void A_store_s_mail_carries_its_colour_its_logo_and_its_name()
    {
        var html = EmailLayout.Render(
            "Your order 2026-00021",
            "Thank you for your order.",
            "Wooden Home",
            new StoreBranding("#8B5E3C", "https://shop.test/api/storefront/store/logo"));

        Assert.Contains("background:#8B5E3C", html, StringComparison.Ordinal);
        Assert.Contains("<img src=\"https://shop.test/api/storefront/store/logo\"", html, StringComparison.Ordinal);
        Assert.Contains("Wooden Home", html, StringComparison.Ordinal);
        Assert.Contains("Your order 2026-00021", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_logo_the_name_is_the_header()
    {
        var html = EmailLayout.Render("You are invited", "Choose a password.", "ShopForge", branding: null);

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains(">ShopForge</span>", html, StringComparison.Ordinal);
        Assert.Contains("background:#18181b", html, StringComparison.Ordinal);
    }

    // A link is the whole point of half these messages, and it has to survive being encoded.
    [Fact]
    public void A_link_becomes_a_link_and_keeps_the_sentence_around_it()
    {
        var html = EmailLayout.Render(
            "Reset your password",
            "Use this link within an hour: https://shop.test/reset-password?token=abc-123. If you did not ask, ignore this.",
            "Wooden Home",
            new StoreBranding("#000000", null));

        Assert.Contains("<a href=\"https://shop.test/reset-password?token=abc-123\"", html, StringComparison.Ordinal);
        Assert.Contains("</a>.", html, StringComparison.Ordinal);
        Assert.Contains("If you did not ask, ignore this.", html, StringComparison.Ordinal);
    }

    // Names and messages are written by people, and some of those people are not friendly.
    [Fact]
    public void Nothing_anybody_typed_can_become_markup()
    {
        var html = EmailLayout.Render(
            "Your order <script>alert(1)</script>",
            "Thank you, <b>friend</b>.",
            "The \"Shop\" <script>",
            new StoreBranding("#000000", null));

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>friend</b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    // A colour comes from whatever a store typed into its theme and ends up inside a style attribute.
    [Theory]
    [InlineData("#12g456")]
    [InlineData("red; background:url(javascript:alert(1))")]
    [InlineData("")]
    public void A_colour_that_is_not_a_colour_is_not_used(string primaryColor)
    {
        var html = EmailLayout.Render("Hello", "Hello.", "Wooden Home", new StoreBranding(primaryColor, null));

        Assert.Contains("background:#18181b", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_line_of_the_message_is_its_own_paragraph()
    {
        var html = EmailLayout.Render("Your order", "Thank you for your order.\nPay by bank transfer.", "Wooden Home", null);

        Assert.Equal(2, html.Split("<p style=").Length - 1);
    }
}
