using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Attributes;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Search;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Admin;

internal static class AdminAttributeEndpoints
{
    public static void MapAdminAttributes(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/attributes", GetAttributesAsync);
        storeAdmin.MapPost("/attributes", CreateAttributeAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPut("/attributes/{attributeId:guid}", UpdateAttributeAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/attributes/{attributeId:guid}/options", AddOptionAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPut("/categories/{categoryId:guid}/attributes", AssignCategoryAttributesAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapGet("/products/{storeProductId:guid}/attributes", GetProductAttributesAsync);
        storeAdmin.MapPut("/products/{storeProductId:guid}/attributes", SetProductAttributesAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
    }

    private static async Task<Ok<List<AdminAttributeResponse>>> GetAttributesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var definitions = await Definitions(dbContext).ToListAsync(cancellationToken);

        return TypedResults.Ok(definitions.Select(AdminAttributeResponse.From).ToList());
    }

    private static async Task<Results<Created<AdminAttributeResponse>, ValidationProblem, ProblemHttpResult>> CreateAttributeAsync(
        CreateAttributeRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var code = request.Code ?? Slugs.Create(request.Name ?? "");
        var options = request.Options ?? [];
        var errors = ValidateSettings(request.Name, request.Type, request.IsFilterable, request.IsSearchable)
            .Check(Slugs.IsValid(code), "code", "Code may contain lower-case letters, digits and single hyphens.")
            .Check(options.Count == 0 || request.Type is AttributeType.Select or AttributeType.MultiSelect, "options", "Only select attributes have options.")
            .Check(options.All(option => Slugs.Create(option).Length > 0), "options", "Every option needs a name with letters or digits.")
            .Check(options.Select(Slugs.Create).Distinct().Count() == options.Count, "options", "Option names must be unique.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<AttributeDefinition>().AnyAsync(existing => existing.Code == code, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "An attribute with this code already exists in this store");
        }

        var definition = new AttributeDefinition(storeContext.StoreId!.Value, code, request.Name!, request.Type!.Value, ToSettings(request));

        foreach (var option in options)
        {
            definition.AddOption(option, Slugs.Create(option));
        }

        dbContext.Add(definition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{definition.StoreId}/attributes/{definition.Id}", AdminAttributeResponse.From(definition));
    }

    private static async Task<Results<Ok<AdminAttributeResponse>, ValidationProblem, NotFound>> UpdateAttributeAsync(
        Guid attributeId,
        UpdateAttributeRequest request,
        DbContext dbContext,
        SearchIndex search,
        CancellationToken cancellationToken)
    {
        var definition = await Definitions(dbContext).SingleOrDefaultAsync(definition => definition.Id == attributeId, cancellationToken);
        var wasSearched = definition?.IsSearchable ?? false;

        if (definition is null)
        {
            return TypedResults.NotFound();
        }

        var errors = ValidateSettings(request.Name, definition.Type, request.IsFilterable, request.IsSearchable);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        definition.Update(request.Name!, new AttributeSettings(request.Unit, request.IsFilterable, request.IsVisibleOnProductPage, request.SortOrder, request.IsInFeeds, request.IsSearchable));
        await dbContext.SaveChangesAsync(cancellationToken);

        // A measurement that has started or stopped being searched changes what every listing of this shop
        // answers to, not just one of them.
        if (wasSearched != definition.IsSearchable)
        {
            await search.RefreshStoreAsync(cancellationToken);
        }

        return TypedResults.Ok(AdminAttributeResponse.From(definition));
    }

    private static async Task<Results<Created<AdminAttributeResponse>, ValidationProblem, NotFound>> AddOptionAsync(
        Guid attributeId,
        AddOptionRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var definition = await Definitions(dbContext).SingleOrDefaultAsync(definition => definition.Id == attributeId, cancellationToken);

        if (definition is null)
        {
            return TypedResults.NotFound();
        }

        var code = request.Code ?? Slugs.Create(request.Name ?? "");
        var errors = new RequestErrors()
            .Check(definition.HasOptions, "attribute", "Only select attributes have options.")
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(Slugs.IsValid(code), "code", "Code may contain lower-case letters, digits and single hyphens.")
            .Check(definition.Options.All(option => option.Code != code), "code", "The attribute already has this option.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        definition.AddOption(request.Name!, code);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{definition.StoreId}/attributes/{definition.Id}", AdminAttributeResponse.From(definition));
    }

    private static async Task<Results<Ok<List<Guid>>, ValidationProblem, NotFound>> AssignCategoryAttributesAsync(
        Guid categoryId,
        AssignAttributesRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Set<Category>()
            .Include(category => category.Attributes)
            .SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var attributeIds = request.AttributeIds ?? [];
        var definitions = await dbContext.Set<AttributeDefinition>().Where(definition => attributeIds.Contains(definition.Id)).ToListAsync(cancellationToken);
        var errors = new RequestErrors()
            .Check(attributeIds.Distinct().Count() == attributeIds.Count, "attributeIds", "Attributes must not repeat.")
            .Check(definitions.Count == attributeIds.Distinct().Count(), "attributeIds", "One or more attributes were not found.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        category.AssignAttributes([.. attributeIds.Select(id => definitions.Single(definition => definition.Id == id))]);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(attributeIds);
    }

    private static async Task<Results<Ok<ProductAttributesResponse>, NotFound>> GetProductAttributesAsync(
        Guid storeProductId,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .AsNoTracking()
            .Include(storeProduct => storeProduct.AttributeValues)
            .SingleOrDefaultAsync(storeProduct => storeProduct.Id == storeProductId, cancellationToken);

        if (storeProduct is null)
        {
            return TypedResults.NotFound();
        }

        var definitions = await Definitions(dbContext).AsNoTracking().ToListAsync(cancellationToken);

        return TypedResults.Ok(ProductAttributesResponse.From(storeProduct, definitions));
    }

    private static async Task<Results<Ok<ProductAttributesResponse>, ValidationProblem, NotFound>> SetProductAttributesAsync(
        Guid storeProductId,
        ProductAttributesRequest request,
        DbContext dbContext,
        SearchIndex search,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .Include(storeProduct => storeProduct.AttributeValues)
            .SingleOrDefaultAsync(storeProduct => storeProduct.Id == storeProductId, cancellationToken);

        if (storeProduct is null)
        {
            return TypedResults.NotFound();
        }

        var definitions = await Definitions(dbContext).ToListAsync(cancellationToken);
        var errors = new RequestErrors();
        var values = new List<(AttributeDefinition Definition, AttributeValue Value)>();

        foreach (var (code, json) in request.Values ?? [])
        {
            var definition = definitions.SingleOrDefault(definition => definition.Code == code);

            if (definition is null)
            {
                errors.Check(false, $"values.{code}", "Unknown attribute.");
            }
            else if (json.ValueKind != JsonValueKind.Null)
            {
                errors.Check(AttributeValueJson.TryRead(definition, json, out var value), $"values.{code}", ExpectedValue(definition.Type));
                values.Add((definition, value));
            }
        }

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        storeProduct.ReplaceAttributeValues(values);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Words a merchant typed into a searchable measurement are words a shopper can type back.
        await search.RefreshAsync(storeProduct.Id, cancellationToken);

        return TypedResults.Ok(ProductAttributesResponse.From(storeProduct, definitions));
    }

    private static IQueryable<AttributeDefinition> Definitions(DbContext dbContext) =>
        dbContext.Set<AttributeDefinition>()
            .Include(definition => definition.Options)
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Name);

    private static RequestErrors ValidateSettings(string? name, AttributeType? type, bool isFilterable, bool isSearchable = false) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(type is not null && Enum.IsDefined(type.Value), "type", "Type is required.")
            .Check(!(isFilterable && type == AttributeType.Text), "isFilterable", "Text attributes cannot be used as filters.")
            .Check(!isSearchable || type == AttributeType.Text, "isSearchable", "Only text attributes have words to search.");

    private static AttributeSettings ToSettings(CreateAttributeRequest request) =>
        new(request.Unit, request.IsFilterable, request.IsVisibleOnProductPage, request.SortOrder, request.IsInFeeds, request.IsSearchable);

    private static string ExpectedValue(AttributeType type) => type switch
    {
        AttributeType.Text => $"Expected text of up to {AttributeDefinition.MaxTextLength} characters.",
        AttributeType.Integer => "Expected a whole number.",
        AttributeType.Decimal => "Expected a number.",
        AttributeType.Boolean => "Expected true or false.",
        AttributeType.Date => "Expected a date in the format yyyy-MM-dd.",
        AttributeType.Select => "Expected the code of one of the attribute's options.",
        _ => "Expected a non-empty list of distinct option codes.",
    };
}

internal sealed record CreateAttributeRequest(
    string? Code,
    string? Name,
    AttributeType? Type,
    string? Unit,
    bool IsFilterable,
    bool IsVisibleOnProductPage,
    int SortOrder,
    List<string>? Options,
    bool IsInFeeds = false,
    bool IsSearchable = false);

internal sealed record UpdateAttributeRequest(
    string? Name, string? Unit, bool IsFilterable, bool IsVisibleOnProductPage, int SortOrder, bool IsInFeeds = false, bool IsSearchable = false);

internal sealed record AddOptionRequest(string? Name, string? Code);

internal sealed record AssignAttributesRequest(List<Guid>? AttributeIds);

internal sealed record ProductAttributesRequest(Dictionary<string, JsonElement>? Values);

internal sealed record ProductAttributesResponse(Dictionary<string, object?> Values)
{
    public static ProductAttributesResponse From(StoreProduct storeProduct, IEnumerable<AttributeDefinition> definitions) => new(
        definitions
            .Select(definition => (definition.Code, Value: AttributeValueJson.Write(
                definition,
                [.. storeProduct.AttributeValues.Where(value => value.AttributeDefinitionId == definition.Id)],
                optionNames: false)))
            .Where(item => item.Value is not null)
            .ToDictionary(item => item.Code, item => item.Value));
}

internal sealed record AdminOptionResponse(Guid Id, string Code, string Name);

internal sealed record AdminAttributeResponse(
    Guid Id,
    string Code,
    string Name,
    AttributeType Type,
    string? Unit,
    bool IsFilterable,
    bool IsVisibleOnProductPage,
    int SortOrder,
    bool IsInFeeds,
    bool IsSearchable,
    List<AdminOptionResponse> Options)
{
    public static AdminAttributeResponse From(AttributeDefinition definition) => new(
        definition.Id,
        definition.Code,
        definition.Name,
        definition.Type,
        definition.Unit,
        definition.IsFilterable,
        definition.IsVisibleOnProductPage,
        definition.SortOrder,
        definition.IsInFeeds,
        definition.IsSearchable,
        [.. definition.Options.OrderBy(option => option.SortOrder).Select(option => new AdminOptionResponse(option.Id, option.Code, option.Name))]);
}
