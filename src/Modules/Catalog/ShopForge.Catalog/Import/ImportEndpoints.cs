using ClosedXML.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Import;

internal static class ImportEndpoints
{
    private const string SpreadsheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapCatalogImport(this IEndpointRouteBuilder storeAdmin)
    {
        // Cookie auth is SameSite=Strict, so cross-site form posts never carry the session; antiforgery tokens add nothing.
        // A whole catalogue costs real work to read, so it gets the narrow window; the body limit is raised to
        // what the handler already accepts, so the server refuses anything bigger before reading it (D-126).
        storeAdmin.MapPost("/import", ImportAsync)
            .RequireAuthorization(AdminPolicies.CatalogManagement)
            .RequireRateLimiting(RateLimits.Expensive)
            .WithMetadata(new RequestSizeLimitAttribute(ImportFile.MaxBytes))
            .DisableAntiforgery();
        storeAdmin.MapGet("/import/template", GetTemplateAsync);
    }

    private static async Task<Results<Ok<ImportReport>, ValidationProblem>> ImportAsync(
        IFormFile? file,
        DbContext dbContext,
        IStoreContext storeContext,
        IStockLedger stock,
        ITenantLimits limits,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (file is not { Length: > 0 } || file.Length > ImportFile.MaxBytes)
        {
            return InvalidFile($"An .xlsx file of at most {ImportFile.MaxBytes / (1024 * 1024)} MB is required.");
        }

        try
        {
            await using var content = file.OpenReadStream();
            var import = ImportFile.Read(content);

            return TypedResults.Ok(await new CatalogImporter(dbContext, storeContext, stock, limits, clock).ImportAsync(import, cancellationToken));
        }
        catch (ImportFileException exception)
        {
            return InvalidFile(exception.Message);
        }
    }

    private static async Task<FileStreamHttpResult> GetTemplateAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var attributeCodes = await dbContext.Set<AttributeDefinition>()
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Name)
            .Select(definition => definition.Code)
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");
        var headers = ImportColumns.All.Concat(attributeCodes).ToList();

        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return TypedResults.File(stream, SpreadsheetContentType, "shopforge-import-template.xlsx");
    }

    private static ValidationProblem InvalidFile(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
