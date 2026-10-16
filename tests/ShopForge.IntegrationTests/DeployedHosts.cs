using Microsoft.AspNetCore.Hosting;

namespace ShopForge.IntegrationTests;

internal static class DeployedHosts
{
    // A host as a deployment runs it. A deployment always names a provider that delivers mail (D-205); the suite's
    // recording delivery still takes every letter, so nothing leaves the test.
    public static IWebHostBuilder AsDeployed(this IWebHostBuilder builder) => builder
        .UseEnvironment("Production")
        .UseSetting("Email:Provider", "mailgun")
        .UseSetting("Email:SenderAddress", "shop@mail.example.test")
        .UseSetting("Email:Mailgun:ApiKey", "made-up-for-the-test")
        .UseSetting("Email:Mailgun:Domain", "mail.example.test");
}
