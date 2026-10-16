using Microsoft.AspNetCore.Hosting;

namespace ShopForge.IntegrationTests.Messaging;

// Mail that is only logged looks like mail that is sent until a customer waits for a confirmation, so a deployment
// that would only log it does not start. Development, the test hosts and the end-to-end stack still may (D-205).
public sealed class MailConfigurationTests(ShopForgeApiFactory factory)
{
    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "log")]
    [InlineData("Staging", "LOG")]
    public void A_deployment_that_would_only_log_its_mail_does_not_start(string environment, string? provider)
    {
        using var deployed = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);

            if (provider is not null)
            {
                builder.UseSetting("Email:Provider", provider);
            }
        });

        var refused = Assert.ThrowsAny<Exception>(() => deployed.Services);

        Assert.Contains("Email:Provider is 'log'", Innermost(refused).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deployment_that_names_a_provider_starts()
    {
        using var deployed = factory.WithWebHostBuilder(builder => builder.AsDeployed());

        Assert.NotNull(deployed.Services);
    }

    private static Exception Innermost(Exception exception) =>
        exception.InnerException is { } inner ? Innermost(inner) : exception;
}
