using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Discounts;

internal sealed class DiscountCodes(DbContext dbContext, TimeProvider clock)
{
    public Task<Discount?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = Discount.Normalize(code);

        return dbContext.Set<Discount>().SingleOrDefaultAsync(discount => discount.Code == normalized, cancellationToken);
    }

    // What the cart can tell the shopper before checkout; the counted limits are settled at placement (D-086).
    public async Task<DiscountProblem?> FindProblemAsync(Discount discount, decimal itemsTotal, string? email, CancellationToken cancellationToken)
    {
        if (discount.FindProblem(itemsTotal, clock.GetUtcNow()) is { } problem)
        {
            return problem;
        }

        return await HasReachedPersonalLimitAsync(discount, email, cancellationToken) ? DiscountProblem.AlreadyUsed : null;
    }

    public async Task<bool> HasReachedPersonalLimitAsync(Discount discount, string? email, CancellationToken cancellationToken)
    {
        if (discount.MaxRedemptionsPerCustomer is not { } limit || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalized = email.Trim().ToLowerInvariant();
        var used = await dbContext.Set<DiscountRedemption>()
            .CountAsync(redemption => redemption.DiscountId == discount.Id && redemption.Email == normalized, cancellationToken);

        return used >= limit;
    }

    // The database decides whether a code still has a redemption left, so two checkouts cannot both take the last
    // one (D-086). Zero rows updated means it is spent.
    public async Task<bool> TryRedeemAsync(Discount discount, CancellationToken cancellationToken)
    {
        var redeemed = await dbContext.Set<Discount>()
            .Where(candidate => candidate.Id == discount.Id
                && candidate.IsActive
                && (candidate.MaxRedemptions == null || candidate.Redemptions < candidate.MaxRedemptions))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.Redemptions, candidate => candidate.Redemptions + 1),
                cancellationToken);

        return redeemed == 1;
    }

    public static string Explain(DiscountProblem problem) => problem switch
    {
        DiscountProblem.NotStarted => "This code is not valid yet.",
        DiscountProblem.Expired => "This code has expired.",
        DiscountProblem.BelowMinimum => "Your order is below the minimum for this code.",
        DiscountProblem.UsedUp => "This code has been used up.",
        DiscountProblem.AlreadyUsed => "You have already used this code.",
        _ => "This code cannot be used.",
    };
}
