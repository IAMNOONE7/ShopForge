using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Tax;

// A shop's tax rules: what its things are, and what each country charges for them. Changes are recorded,
// because a rate is the kind of number somebody later has to explain (Stage 17, D-187).
internal static class AdminTaxEndpoints
{
    public static IEndpointRouteBuilder MapAdminTaxRules(this IEndpointRouteBuilder storeAdmin)
    {
        var classes = storeAdmin.MapGroup("/tax-classes").RequireAuthorization(AdminPolicies.StoreManagement);

        classes.MapGet("/", GetClassesAsync);
        classes.MapPost("/", CreateClassAsync);
        classes.MapPut("/{code}", RenameClassAsync);
        classes.MapDelete("/{code}", DeleteClassAsync);
        classes.MapPut("/{code}/rates/{country}", SetRateAsync);
        classes.MapDelete("/{code}/rates/{country}", RemoveRateAsync);

        return storeAdmin;
    }

    private static async Task<Ok<List<AdminTaxClassResponse>>> GetClassesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var classes = await dbContext.Set<TaxClass>()
            .AsNoTracking()
            .OrderBy(taxClass => taxClass.Name)
            .Select(taxClass => new { taxClass.Code, taxClass.Name })
            .ToListAsync(cancellationToken);

        var rates = await dbContext.Set<TaxRate>()
            .AsNoTracking()
            .OrderBy(rate => rate.Country)
            .Select(rate => new { rate.TaxClass, rate.Country, rate.Rate })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(classes
            .Select(taxClass => new AdminTaxClassResponse(
                taxClass.Code,
                taxClass.Name,
                [.. rates.Where(rate => rate.TaxClass == taxClass.Code).Select(rate => new AdminTaxRateResponse(rate.Country, rate.Rate))]))
            .ToList());
    }

    private static async Task<Results<Created<AdminTaxClassResponse>, ValidationProblem, ProblemHttpResult>> CreateClassAsync(
        TaxClassRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var code = request.Code is { Length: > 0 } given ? Slugs.Create(given) : Slugs.Create(request.Name ?? string.Empty);
        var errors = ValidateName(request.Name)
            .Check(code.Length is > 0 and <= TaxClass.MaxCodeLength, "code", "A code of lower-case letters, digits and hyphens is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<TaxClass>().AnyAsync(existing => existing.Code == code, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The store already has a tax class with this code");
        }

        var taxClass = new TaxClass(storeContext.StoreId!.Value, code, request.Name!);
        dbContext.Add(taxClass);
        audit.Record("store.tax_class.added", code, new { taxClass.Name });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{taxClass.StoreId}/tax-classes/{taxClass.Code}",
            new AdminTaxClassResponse(taxClass.Code, taxClass.Name, []));
    }

    private static async Task<Results<Ok<AdminTaxClassResponse>, ValidationProblem, NotFound>> RenameClassAsync(
        string code,
        TaxClassRequest request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var taxClass = await dbContext.Set<TaxClass>().SingleOrDefaultAsync(existing => existing.Code == code, cancellationToken);

        if (taxClass is null)
        {
            return TypedResults.NotFound();
        }

        // The code is what a listing names, so only the label changes here (D-181).
        taxClass.Rename(request.Name!);
        audit.Record("store.tax_class.renamed", code, new { taxClass.Name });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminTaxClassResponse(taxClass.Code, taxClass.Name, []));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteClassAsync(
        string code,
        DbContext dbContext,
        IEnumerable<ITaxClassUsers> users,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var taxClass = await dbContext.Set<TaxClass>().SingleOrDefaultAsync(existing => existing.Code == code, cancellationToken);

        if (taxClass is null)
        {
            return TypedResults.NotFound();
        }

        foreach (var module in users)
        {
            if (await module.WhatNamesAsync(code, cancellationToken) is { } reason)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This tax class cannot be deleted",
                    detail: $"It cannot be deleted because {reason}. Point those at another class first.");
            }
        }

        dbContext.Remove(taxClass);
        await dbContext.Set<TaxRate>().Where(rate => rate.TaxClass == code).ExecuteDeleteAsync(cancellationToken);
        audit.Record("store.tax_class.removed", code);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AdminTaxRateResponse>, ValidationProblem, NotFound>> SetRateAsync(
        string code,
        string country,
        TaxRateRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(Countries.IsValid(country), "country", "A country is a two-letter code, for example DE.")
            .Check(request.Rate is >= 0 and <= 100 && decimal.Round(request.Rate, 2) == request.Rate, "rate", "A rate is between 0 and 100, with at most two decimals.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (!await dbContext.Set<TaxClass>().AnyAsync(existing => existing.Code == code, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var normalized = Countries.Of(country);
        var existingRate = await dbContext.Set<TaxRate>()
            .SingleOrDefaultAsync(rate => rate.TaxClass == code && rate.Country == normalized, cancellationToken);

        if (existingRate is null)
        {
            dbContext.Add(new TaxRate(storeContext.StoreId!.Value, code, normalized, request.Rate));
            audit.Record("store.tax_rate.set", $"{code}/{normalized}", new { request.Rate });
        }
        else if (existingRate.Set(request.Rate))
        {
            // Only a change is recorded: setting a rate to what it already was is not a decision anybody
            // later has to explain (D-183).
            audit.Record("store.tax_rate.set", $"{code}/{normalized}", new { request.Rate });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminTaxRateResponse(normalized, request.Rate));
    }

    private static async Task<Results<NoContent, ValidationProblem, NotFound>> RemoveRateAsync(
        string code,
        string country,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(Countries.IsValid(country), "country", "A country is a two-letter code, for example DE.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var normalized = Countries.Of(country);
        var rate = await dbContext.Set<TaxRate>()
            .SingleOrDefaultAsync(candidate => candidate.TaxClass == code && candidate.Country == normalized, cancellationToken);

        if (rate is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Remove(rate);
        audit.Record("store.tax_rate.removed", $"{code}/{normalized}");
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static RequestErrors ValidateName(string? name) =>
        new RequestErrors().Check(
            !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= TaxClass.MaxNameLength,
            "name",
            $"Name is required (up to {TaxClass.MaxNameLength} characters).");
}

internal sealed record TaxClassRequest(string? Name, string? Code = null);

internal sealed record TaxRateRequest(decimal Rate);

internal sealed record AdminTaxClassResponse(string Code, string Name, List<AdminTaxRateResponse> Rates);

internal sealed record AdminTaxRateResponse(string Country, decimal Rate);
