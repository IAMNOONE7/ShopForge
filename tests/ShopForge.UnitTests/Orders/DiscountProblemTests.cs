using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Orders;

// A code that would take nothing off is refused, so a code that can be used once is not spent for nothing (D-221).
public sealed class DiscountProblemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_free_shipping_code_is_refused_while_delivery_is_already_free()
    {
        var code = Code(DiscountKind.FreeShipping, 0m);

        Assert.Equal(DiscountProblem.DeliveryAlreadyFree, code.FindProblem(1_500m, Now, deliveryIsFree: true));
        Assert.Null(code.FindProblem(1_500m, Now, deliveryIsFree: false));
    }

    // A code that takes something off the goods still does, whatever delivery costs.
    [Theory]
    [InlineData(nameof(DiscountKind.Percentage), 10)]
    [InlineData(nameof(DiscountKind.Amount), 100)]
    public void A_code_off_the_goods_is_not_refused_for_free_delivery(string kind, decimal value)
    {
        Assert.Null(Code(Enum.Parse<DiscountKind>(kind), value).FindProblem(1_500m, Now, deliveryIsFree: true));
    }

    private static Discount Code(DiscountKind kind, decimal value) =>
        new(Guid.NewGuid(), "SAVE", "Save", kind, value, new DiscountLimits(), Currency.Of("CZK"));
}
