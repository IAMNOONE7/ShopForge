using System.Net.Http.Headers;
using ClosedXML.Excel;

namespace ShopForge.IntegrationTests.Catalog;

internal static class ImportFiles
{
    public static MultipartFormDataContent Workbook(string[] columns, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");

        for (var column = 0; column < columns.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = columns[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = rows[row][column] switch
                {
                    null => XLCellValue.FromObject(null),
                    string text => text,
                    bool flag => flag,
                    DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                    int number => number,
                    decimal number => number,
                    var other => XLCellValue.FromObject(other),
                };
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return Upload(stream.ToArray(), "products.xlsx");
    }

    // What every product needs before an import takes it (D-214), for a test whose subject is something else: the
    // weight and sizes the shared fixtures are created with, so a row about one of their products changes nothing.
    public static readonly string[] MeasurementColumns = ["weight", "package_length_mm", "package_width_mm", "package_height_mm"];

    public static readonly object?[] Measurements = [850, 450, 400, 90];

    public static MultipartFormDataContent MeasuredWorkbook(string[] columns, params object?[][] rows) =>
        Workbook([.. columns, .. MeasurementColumns], [.. rows.Select(row => (object?[])[.. row, .. Measurements])]);

    public static MultipartFormDataContent Upload(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", fileName);

        return form;
    }

    public static Task<HttpResponseMessage> ImportAsync(this HttpClient admin, Guid storeId, MultipartFormDataContent file) =>
        admin.PostAsync($"/api/admin/stores/{storeId}/import", file);
}
