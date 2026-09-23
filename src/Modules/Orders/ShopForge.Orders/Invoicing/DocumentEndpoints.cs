using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Documents;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Invoicing;

// The same document is downloaded by the shopper who bought it and by the store that sold it; only the way they are
// allowed to ask differs.
internal static class DocumentEndpoints
{
    public static void MapStorefrontDocuments(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/orders/{orderNumber}/documents/{documentNumber}", GetWithTokenAsync);
        storefront.MapGet("/account/orders/{orderNumber}/documents/{documentNumber}", GetForCustomerAsync)
            .RequireAuthorization(CustomerPolicies.Customer);
    }

    public static void MapAdminDocuments(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/orders/{orderNumber}/documents/{documentNumber}", GetForAdminAsync);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> GetWithTokenAsync(
        string orderNumber,
        string documentNumber,
        Guid token,
        DbContext dbContext,
        Invoices invoices,
        IDocumentRenderer renderer,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Number == orderNumber && candidate.AccessToken == token, cancellationToken);

        return await DocumentAsync(order, documentNumber, dbContext, invoices, renderer, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> GetForCustomerAsync(
        string orderNumber,
        string documentNumber,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        Invoices invoices,
        IDocumentRenderer renderer,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindStoreCustomerIdAsync(cancellationToken) is not { } storeCustomerId)
        {
            return TypedResults.NotFound();
        }

        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Number == orderNumber && candidate.StoreCustomerId == storeCustomerId, cancellationToken);

        return await DocumentAsync(order, documentNumber, dbContext, invoices, renderer, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> GetForAdminAsync(
        string orderNumber,
        string documentNumber,
        DbContext dbContext,
        Invoices invoices,
        IDocumentRenderer renderer,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Number == orderNumber, cancellationToken);

        return await DocumentAsync(order, documentNumber, dbContext, invoices, renderer, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> DocumentAsync(
        Order? order,
        string documentNumber,
        DbContext dbContext,
        Invoices invoices,
        IDocumentRenderer renderer,
        CancellationToken cancellationToken)
    {
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var invoice = await dbContext.Set<Invoice>()
            .SingleOrDefaultAsync(candidate => candidate.Number == documentNumber && candidate.OrderNumber == order.Number, cancellationToken);

        if (invoice is null)
        {
            return TypedResults.NotFound();
        }

        var pdf = renderer.Render(await invoices.ToDocumentAsync(invoice, cancellationToken));

        return TypedResults.File(pdf, "application/pdf", $"{invoice.Number}.pdf");
    }
}
