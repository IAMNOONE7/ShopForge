using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using ShopForge.Shared.Documents;

namespace ShopForge.Infrastructure.Documents;

internal sealed class MigraDocRenderer : IDocumentRenderer
{
    static MigraDocRenderer() => GlobalFontSettings.FontResolver = new DocumentFonts();

    public byte[] Render(TaxDocument document)
    {
        var culture = CultureInfo.GetCultureInfo(document.CultureName);
        var pdf = new Document();
        var section = pdf.AddSection();
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        var heading = section.AddParagraph($"{document.Title} {document.Number}");
        heading.Format.Font.Size = 18;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromMillimeter(4);

        section.AddParagraph($"Order {document.OrderNumber} · issued {document.IssuedAt.ToString("d MMMM yyyy", culture)}")
            .Format.SpaceAfter = Unit.FromMillimeter(8);

        var parties = section.AddTable();
        parties.AddColumn(Unit.FromCentimeter(8));
        parties.AddColumn(Unit.FromCentimeter(8));
        var partyRow = parties.AddRow();
        AddParty(partyRow.Cells[0], "Seller", document.Seller);
        AddParty(partyRow.Cells[1], "Buyer", document.Buyer);
        parties.Format.SpaceAfter = Unit.FromMillimeter(8);

        var lines = section.AddTable();
        lines.Borders.Bottom.Width = 0.5;
        lines.AddColumn(Unit.FromCentimeter(8));
        lines.AddColumn(Unit.FromCentimeter(2));
        lines.AddColumn(Unit.FromCentimeter(3));
        lines.AddColumn(Unit.FromCentimeter(3));
        AddRow(lines, bold: true, "Description", "Quantity", "VAT", "Total");

        foreach (var line in document.Lines)
        {
            AddRow(
                lines,
                bold: false,
                line.Description,
                line.Quantity.ToString(culture),
                $"{line.VatRate.ToString("0.##", culture)} %",
                Money(line.LineTotal, document.Currency, culture));
        }

        lines.Format.SpaceAfter = Unit.FromMillimeter(8);

        var summary = section.AddTable();
        summary.AddColumn(Unit.FromCentimeter(4));
        summary.AddColumn(Unit.FromCentimeter(4));
        summary.AddColumn(Unit.FromCentimeter(4));
        summary.AddColumn(Unit.FromCentimeter(4));
        AddRow(summary, bold: true, "VAT rate", "Net", "VAT", "Gross");

        foreach (var rate in document.VatSummary)
        {
            AddRow(
                summary,
                bold: false,
                $"{rate.Rate.ToString("0.##", culture)} %",
                Money(rate.Net, document.Currency, culture),
                Money(rate.Vat, document.Currency, culture),
                Money(rate.Gross, document.Currency, culture));
        }

        AddRow(
            summary,
            bold: true,
            "Total",
            Money(document.Net, document.Currency, culture),
            Money(document.Vat, document.Currency, culture),
            Money(document.Total, document.Currency, culture));

        section.AddParagraph($"Paid by {document.PaymentMethod}.").Format.SpaceBefore = Unit.FromMillimeter(8);

        var renderer = new MigraDoc.Rendering.PdfDocumentRenderer { Document = pdf };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream);

        return stream.ToArray();
    }

    private static void AddParty(Cell cell, string title, DocumentParty party)
    {
        cell.AddParagraph(title).Format.Font.Bold = true;
        cell.AddParagraph(party.Name);

        foreach (var line in party.AddressLines)
        {
            cell.AddParagraph(line);
        }

        if (party.RegistrationNumber is { Length: > 0 })
        {
            cell.AddParagraph($"Reg. no. {party.RegistrationNumber}");
        }

        if (party.VatNumber is { Length: > 0 })
        {
            cell.AddParagraph($"VAT no. {party.VatNumber}");
        }
    }

    private static void AddRow(Table table, bool bold, params string[] values)
    {
        var row = table.AddRow();
        row.Format.Font.Bold = bold;

        for (var column = 0; column < values.Length; column++)
        {
            row.Cells[column].AddParagraph(values[column]);
        }
    }

    private static string Money(decimal amount, string currency, CultureInfo culture) =>
        $"{amount.ToString("N2", culture)} {currency}";
}

// PDFsharp has no fonts of its own outside Windows; the container installs DejaVu and this hands the file over.
internal sealed class DocumentFonts : IFontResolver
{
    private static readonly string[] SearchPaths =
    [
        "/usr/share/fonts/truetype/dejavu",
        "/usr/share/fonts/dejavu",
        "/usr/share/fonts/TTF",
    ];

    public byte[]? GetFont(string faceName) =>
        FindFile(faceName) is { } path ? File.ReadAllBytes(path) : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");

    private static string? FindFile(string faceName) =>
        SearchPaths.Select(directory => Path.Combine(directory, faceName)).FirstOrDefault(File.Exists);
}
