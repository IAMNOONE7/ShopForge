using ShopForge.Catalog.Domain;

namespace ShopForge.UnitTests.Catalog;

// The rules a hierarchy has to keep, settled without a database (D-172).
public sealed class CategoryTreeTests
{
    private static readonly Guid Furniture = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Chairs = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Dining = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Tables = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Lighting = Guid.Parse("00000000-0000-0000-0000-000000000005");

    // Furniture > Chairs > Dining, Furniture > Tables, and Lighting on its own.
    private static CategoryTree Shop() => CategoryTree.Of(
    [
        new CategoryPlace(Furniture, null),
        new CategoryPlace(Chairs, Furniture),
        new CategoryPlace(Dining, Chairs),
        new CategoryPlace(Tables, Furniture),
        new CategoryPlace(Lighting, null),
    ]);

    [Fact]
    public void Categories_with_no_parent_are_the_tops_of_the_tree()
    {
        Assert.Equal([Furniture, Lighting], Shop().Roots);
    }

    [Fact]
    public void A_path_reads_from_the_top_down()
    {
        Assert.Equal([Furniture, Chairs, Dining], Shop().PathTo(Dining));
        Assert.Equal([Lighting], Shop().PathTo(Lighting));
        Assert.Equal(3, Shop().DepthOf(Dining));
    }

    // What a parent category sells (D-146): itself and everything under it, each named once.
    [Fact]
    public void A_category_and_everything_beneath_it_is_each_named_once()
    {
        var beneath = Shop().AndBeneath(Furniture);

        Assert.Equal([Furniture, Chairs, Tables, Dining], beneath);
        Assert.Equal(beneath.Distinct().Count(), beneath.Count);
        Assert.DoesNotContain(Lighting, beneath);
        Assert.Equal([Dining], Shop().AndBeneath(Dining));
    }

    [Fact]
    public void A_flat_list_puts_every_parent_before_its_children()
    {
        var everything = Shop().Everything().ToList();

        Assert.Equal(5, everything.Count);
        Assert.Equal([Furniture, Chairs, Dining, Tables, Lighting], everything);
    }

    [Fact]
    public void A_category_cannot_be_its_own_parent()
    {
        Assert.False(Shop().CanAdopt(Chairs, Chairs));
    }

    [Fact]
    public void A_category_cannot_move_beneath_one_of_its_own()
    {
        Assert.False(Shop().CanAdopt(Furniture, Dining));
        Assert.False(Shop().CanAdopt(Chairs, Dining));
    }

    [Fact]
    public void Moving_to_the_top_is_always_allowed_and_so_is_moving_sideways()
    {
        Assert.True(Shop().CanAdopt(Dining, null));
        Assert.True(Shop().CanAdopt(Dining, Tables));
        Assert.True(Shop().CanAdopt(Lighting, Chairs));
    }

    [Fact]
    public void A_parent_nothing_in_this_shop_answers_for_is_refused()
    {
        Assert.False(Shop().CanAdopt(Chairs, Guid.NewGuid()));
    }

    // The case neither end breaks alone: the Furniture branch is three levels tall, so it fits under a
    // category two deep and not under one three deep, though neither of them is too deep by itself.
    [Fact]
    public void A_move_that_would_push_a_branch_past_the_limit_is_refused()
    {
        Assert.Equal(3, Shop().HeightOf(Furniture));
        Assert.Equal(5, CategoryTree.MaxDepth);

        Assert.True(ShopUnder(2).CanAdopt(Furniture, Rung(2)));
        Assert.False(ShopUnder(3).CanAdopt(Furniture, Rung(3)));
    }

    [Fact]
    public void A_category_at_the_limit_cannot_take_a_child_and_one_short_of_it_can()
    {
        Assert.False(Ladder(CategoryTree.MaxDepth).CanHoldAChild(Rung(CategoryTree.MaxDepth)));
        Assert.True(Ladder(CategoryTree.MaxDepth - 1).CanHoldAChild(Rung(CategoryTree.MaxDepth - 1)));
    }

    // Nothing in the application can write a cycle, so one in the data came from something that bypassed
    // these rules. It must not hang the page that reads it: the cycle is broken into tops instead.
    [Fact]
    public void A_cycle_in_the_data_is_read_as_separate_tops_rather_than_hanging()
    {
        var left = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var right = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

        var broken = CategoryTree.Of([new CategoryPlace(left, right), new CategoryPlace(right, left)]);

        Assert.Equal(2, broken.Roots.Count);
        Assert.Equal(2, broken.Everything().Count);
        Assert.Equal([left], broken.PathTo(left));
        Assert.Equal(1, broken.HeightOf(left));
    }

    [Fact]
    public void A_category_the_tree_has_never_heard_of_has_no_path_and_nothing_beneath_it()
    {
        var stranger = Guid.NewGuid();

        Assert.Empty(Shop().PathTo(stranger));
        Assert.Empty(Shop().AndBeneath(stranger));
        Assert.False(Shop().Knows(stranger));
    }

    // A single line of categories, `rungs` deep, with ids of their own.
    private static CategoryTree Ladder(int rungs) => CategoryTree.Of([.. Rungs(rungs)]);

    // That same line, with the three-level Furniture branch standing beside it waiting to be moved.
    private static CategoryTree ShopUnder(int rungs) => CategoryTree.Of(
    [
        .. Rungs(rungs),
        new CategoryPlace(Furniture, null),
        new CategoryPlace(Chairs, Furniture),
        new CategoryPlace(Dining, Chairs),
    ]);

    private static IEnumerable<CategoryPlace> Rungs(int rungs) =>
        Enumerable.Range(1, rungs).Select(rung => new CategoryPlace(Rung(rung), rung == 1 ? null : Rung(rung - 1)));

    private static Guid Rung(int rung) => Guid.Parse($"00000000-0000-0000-0000-0000000000b{rung:d1}");
}
