using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Export;

// Orders as a spreadsheet, one row per order. There is no importer to agree with, so the columns are the ones
// an accountant asks for: what was ordered, by whom, what it came to, and what has been given back (D-185).
//
// A line-level export — one row per thing sold — answers a different question and is a second export rather
// than more columns on this one.
internal static class OrderExport
{
    // The file is built in memory, so a merchant exporting a decade gets the most recent of it rather than a
    // server that stops answering. The window is what keeps a real export small.
    public const int MostRows = 20_000;

    private static readonly string[] Headers =
    [
        "number", "placed", "status", "email", "phone", "currency",
        "payment method", "shipping method", "carrier",
        "items total", "vat total", "shipping", "discount", "total", "refunded",
        "billing name", "billing line1", "billing city", "billing postcode", "billing country",
        "shipping name", "shipping line1", "shipping city", "shipping postcode", "shipping country",
        "pickup point",
    ];

    public static async Task<MemoryStream> WriteAsync(
        DbContext dbContext,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var orders = await dbContext.Set<Order>()
            .AsNoTracking()
            .Where(order => (from == null || order.PlacedAt >= from) && (to == null || order.PlacedAt < to))
            .OrderByDescending(order => order.PlacedAt)
            .ThenByDescending(order => order.Number)
            .Take(MostRows)
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Orders");

        for (var column = 0; column < Headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = Headers[column];
        }

        var row = 2;

        foreach (var order in orders)
        {
            Fill(sheet, row++, order);
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    private static void Fill(IXLWorksheet sheet, int row, Order order)
    {
        var column = 1;

        void Put(object? value) => sheet.Cell(row, column++).Value = value switch
        {
            null => Blank.Value,
            string text => text,
            decimal number => number,
            DateTimeOffset moment => moment.UtcDateTime,
            _ => value.ToString(),
        };

        Put(order.Number);
        Put(order.PlacedAt);
        Put(order.Status.ToString());
        Put(order.Email);
        Put(order.Phone);
        Put(order.Currency);
        Put(order.PaymentMethodName);
        Put(order.ShippingMethodName);
        Put(order.ShippingProviderKey);
        Put(order.ItemsTotal);
        Put(order.VatTotal);
        Put(order.ShippingCharged);
        Put(order.DiscountTotal);
        Put(order.GrandTotal);
        Put(order.RefundedTotal);
        Address(Put, order.BillingAddress);
        Address(Put, order.ShippingAddress);
        Put(order.PickupPointName);
    }

    private static void Address(Action<object?> put, Address address)
    {
        put(address.FullName);
        put(address.Line1);
        put(address.City);
        put(address.PostalCode);
        put(address.Country);
    }
}
