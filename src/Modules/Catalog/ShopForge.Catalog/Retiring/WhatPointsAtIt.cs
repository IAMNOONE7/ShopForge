using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Feeds;
using ShopForge.Shared.Catalog;

namespace ShopForge.Catalog.Retiring;

// Whether a thing can be deleted, and if not, what is holding it. A merchant deleting something is entitled
// to be told what it would break rather than to a refusal with no reason, and to archive it instead (D-180).
//
// What history depends on is never deleted: an order names the listing it sold, so a listing that has ever
// been bought is archived and not removed. What nothing points at goes properly, because a catalogue full of
// retired mistakes is its own kind of mess.
internal sealed class WhatPointsAtIt(DbContext dbContext, ISoldListings sold)
{
    public async Task<string?> HoldingAListingAsync(Guid storeProductId, CancellationToken cancellationToken)
    {
        // Orders live in another module, so what has been sold arrives through a seam.
        if (await sold.HasBeenOrderedAsync(storeProductId, cancellationToken))
        {
            return "it has been ordered";
        }

        if (await dbContext.Set<SlugHistory>().AnyAsync(
                history => history.Kind == SlugKind.Listing && history.PointsAt == storeProductId, cancellationToken))
        {
            return "an address it used to answer to still points at it";
        }

        return null;
    }

    public async Task<string?> HoldingACategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<Category>().AnyAsync(category => category.ParentId == categoryId, cancellationToken))
        {
            return "another category sits beneath it";
        }

        if (await dbContext.Set<ProductCategory>().AnyAsync(assignment => assignment.CategoryId == categoryId, cancellationToken))
        {
            return "listings are in it";
        }

        if (await dbContext.Set<CategoryFeedMapping>().AnyAsync(mapping => mapping.CategoryId == categoryId, cancellationToken))
        {
            return "a shopping engine's own name has been mapped to it";
        }

        if (await dbContext.Set<SlugHistory>().AnyAsync(
                history => history.Kind == SlugKind.Category && history.PointsAt == categoryId, cancellationToken))
        {
            return "an address it used to answer to still points at it";
        }

        return null;
    }

    // A measurement is held by the listings that have one and by the categories that offer it as a filter.
    // The count is in the message: "fourteen listings have a width" tells a merchant what clearing it costs,
    // where "it is in use" tells them nothing (D-181).
    public async Task<string?> HoldingAnAttributeAsync(Guid attributeId, CancellationToken cancellationToken)
    {
        var listings = await dbContext.Set<ProductAttributeValue>()
            .Where(value => value.AttributeDefinitionId == attributeId)
            .Select(value => value.StoreProductId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (listings > 0)
        {
            return $"{listings} listing{(listings == 1 ? "" : "s")} {(listings == 1 ? "has" : "have")} a value for it";
        }

        var categories = await dbContext.Set<CategoryAttribute>()
            .CountAsync(assignment => assignment.AttributeDefinitionId == attributeId, cancellationToken);

        return categories > 0
            ? $"{categories} categor{(categories == 1 ? "y" : "ies")} offer{(categories == 1 ? "s" : "")} it as a filter"
            : null;
    }

    // An option is held only by what has been chosen: a value naming it. Take the option away and a listing
    // would point at nothing.
    public async Task<string?> HoldingAnOptionAsync(Guid optionId, CancellationToken cancellationToken)
    {
        var listings = await dbContext.Set<ProductAttributeValue>()
            .Where(value => value.OptionId == optionId)
            .Select(value => value.StoreProductId)
            .Distinct()
            .CountAsync(cancellationToken);

        return listings > 0
            ? $"{listings} listing{(listings == 1 ? "" : "s")} {(listings == 1 ? "is" : "are")} set to it"
            : null;
    }

    public async Task<string?> HoldingAProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<StoreProduct>().IgnoreQueryFilters().AnyAsync(
                listing => listing.ProductId == productId, cancellationToken))
        {
            return "a shop lists it";
        }

        return null;
    }
}
