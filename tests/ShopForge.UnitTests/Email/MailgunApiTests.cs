using System.Net;
using ShopForge.Infrastructure.Email;

namespace ShopForge.UnitTests.Email;

// Which of Mailgun's answers stop an operation for a person and which are tried again, and which region a deployment
// sends from (D-210, D-211).
public sealed class MailgunApiTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public void A_refusal_Mailgun_would_give_again_stops_and_anything_else_is_tried_again(HttpStatusCode status, bool permanent)
    {
        var refusal = new MailgunException(status, "no");

        Assert.Equal(permanent, refusal.IsPermanent);
        Assert.StartsWith($"Mailgun answered {(int)status}: no", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://api.eu.mailgun.net", "eu")]
    [InlineData("https://api.mailgun.net", "us")]
    public void A_deployment_s_region_is_its_API_s(string baseUrl, string region) =>
        Assert.Equal(region, MailgunApi.RegionOf(new EmailOptions { Mailgun = new MailgunOptions { BaseUrl = baseUrl } }));
}
