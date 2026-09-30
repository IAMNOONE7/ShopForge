using Microsoft.AspNetCore.DataProtection;
using ShopForge.Shared.Security;

namespace ShopForge.UnitTests.Security;

public sealed class TwoFactorTicketsTests
{
    // A ticket's life is measured against the real clock by the data protector, so the moment it was issued has
    // to be a real one: a fixed date makes the test pass until that date goes by.
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    // The two sign-in domains protect their tickets under different purposes, so half a sign-in as a company's
    // staff is not half a sign-in as an operator, whatever the ids inside happen to be (D-129).
    [Fact]
    public void A_ticket_cannot_be_read_under_the_other_domains_purpose()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var userId = Guid.NewGuid();
        var stamp = Guid.NewGuid();

        var ticket = TwoFactorTickets.Issue(dataProtection, TwoFactorTickets.TenantUser, userId, stamp, Now);

        var elsewhere = TwoFactorTickets.Read(dataProtection, TwoFactorTickets.PlatformUser, ticket);
        var athome = TwoFactorTickets.Read(dataProtection, TwoFactorTickets.TenantUser, ticket);

        Assert.Null(elsewhere);
        Assert.Equal(userId, athome!.UserId);
        Assert.Equal(stamp, athome.SecurityStamp);
    }

    [Fact]
    public void A_ticket_nobody_used_in_time_is_worth_nothing()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var anHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var ticket = TwoFactorTickets.Issue(dataProtection, TwoFactorTickets.PlatformUser, Guid.NewGuid(), Guid.NewGuid(), anHourAgo);

        Assert.Null(TwoFactorTickets.Read(dataProtection, TwoFactorTickets.PlatformUser, ticket));
    }
}
