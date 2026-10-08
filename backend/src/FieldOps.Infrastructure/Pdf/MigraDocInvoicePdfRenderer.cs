using FieldOps.Application.Features.InvoiceDelivery;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>
/// Lays out a composed <see cref="InvoicePdfDocument"/> with PDFsharp-MigraDoc in the embedded Inter font
/// (invoice-draft-delivery BR-12). A draft carries a visible "DRAFT" mark in the header of every page.
/// </summary>
public sealed class MigraDocInvoicePdfRenderer : IInvoicePdfRenderer
{
    private static readonly Color Ink = Color.FromRgb(15, 23, 42);

    private static readonly Color Muted = Color.FromRgb(100, 116, 139);

    private static readonly Color Rule = Color.FromRgb(226, 232, 240);

    private static readonly Color Brand = Color.FromRgb(37, 99, 235);

    private static readonly Color Alert = Color.FromRgb(185, 28, 28);

    public byte[] Render(InvoicePdfDocument model)
    {
        InterFontResolver.Register();

        var document = new Document();
        document.Info.Title = model.Title;
        document.Info.Author = model.OrganizationName;

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = InterFontResolver.FamilyName;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.2);

        if (model.IsDraft)
        {
            // Header and footer repeat on every page, so the mark is on all of them.
            Mark(section.Headers.Primary, ParagraphAlignment.Right, 14);
            Mark(section.Footers.Primary, ParagraphAlignment.Center, 10);
        }

        var powered = section.Footers.Primary.AddParagraph(model.PoweredBy);
        powered.Format.Font.Size = 8;
        powered.Format.Font.Color = Muted;
        powered.Format.Alignment = ParagraphAlignment.Center;

        if (model.Logo is { Length: > 0 } logo)
        {
            var image = section.AddImage("base64:" + Convert.ToBase64String(logo));
            image.LockAspectRatio = true;
            image.Height = Unit.FromCentimeter(1.6);
        }

        Text(section, model.OrganizationName, 17, bold: true);

        foreach (var line in model.OrganizationLines)
        {
            Text(section, line, 9.5, color: Muted);
        }

        var title = Text(section, "INVOICE", 13, bold: true, color: Brand);
        title.Format.SpaceBefore = Unit.FromPoint(10);

        MetaTable(section, model.Meta);

        Heading(section, model.BillToHeading);

        foreach (var line in model.BillToLines)
        {
            Text(section, line, 9.5);
        }

        if (!string.IsNullOrWhiteSpace(model.ServiceAddress))
        {
            Heading(section, "Service address");
            Text(section, model.ServiceAddress, 9.5);
        }

        Heading(section, "Items");
        LinesTable(section, model.Lines);
        TotalsTable(section, model.Totals);

        if (!string.IsNullOrWhiteSpace(model.CompletionNote))
        {
            Heading(section, "Service completion note");
            Text(section, model.CompletionNote, 9.5);
        }

        var thanks = Text(section, model.ThankYou, 10, bold: true);
        thanks.Format.SpaceBefore = Unit.FromPoint(16);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);

        return stream.ToArray();
    }

    private static void Mark(HeaderFooter area, ParagraphAlignment alignment, double size)
    {
        var mark = area.AddParagraph("DRAFT");
        mark.Format.Font.Size = size;
        mark.Format.Font.Bold = true;
        mark.Format.Font.Color = Alert;
        mark.Format.Alignment = alignment;
    }

    private static Paragraph Text(Section section, string text, double size, bool bold = false, Color? color = null)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = bold;

        if (color is { } value)
        {
            paragraph.Format.Font.Color = value;
        }

        return paragraph;
    }

    private static void Heading(Section section, string text)
    {
        var heading = Text(section, text, 11, bold: true);
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        heading.Format.SpaceAfter = Unit.FromPoint(4);
        heading.Format.KeepWithNext = true;
    }

    private static void MetaTable(Section section, IReadOnlyList<(string Label, string Value)> meta)
    {
        var table = section.AddTable();
        table.Format.SpaceAfter = Unit.FromPoint(0);
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(13.5));

        foreach (var (label, value) in meta)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(label).Format.Font.Color = Muted;
            row.Cells[1].AddParagraph(value);
        }
    }

    private static void LinesTable(Section section, IReadOnlyList<InvoicePdfLine> lines)
    {
        var table = section.AddTable();
        table.Borders.Bottom.Color = Rule;
        table.Borders.Bottom.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(6.5));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(3));
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(3.5));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Format.Font.Color = Muted;
        header.Cells[0].AddParagraph("Description");
        header.Cells[1].AddParagraph("Qty");
        header.Cells[2].AddParagraph("Rate").Format.Alignment = ParagraphAlignment.Right;
        header.Cells[3].AddParagraph("Tax").Format.Alignment = ParagraphAlignment.Right;
        header.Cells[4].AddParagraph("Amount").Format.Alignment = ParagraphAlignment.Right;

        foreach (var line in lines)
        {
            var row = table.AddRow();
            row.KeepWith = 0;
            row.Cells[0].AddParagraph(line.Description);

            if (line.Detail is not null)
            {
                var detail = row.Cells[0].AddParagraph(line.Detail);
                detail.Format.Font.Size = 8.5;
                detail.Format.Font.Color = Muted;
            }

            row.Cells[1].AddParagraph(line.Quantity);
            row.Cells[2].AddParagraph(line.Rate).Format.Alignment = ParagraphAlignment.Right;
            row.Cells[3].AddParagraph(line.Tax).Format.Alignment = ParagraphAlignment.Right;
            row.Cells[4].AddParagraph(line.Amount).Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void TotalsTable(Section section, IReadOnlyList<InvoicePdfTotal> totals)
    {
        var table = section.AddTable();
        table.Format.SpaceBefore = Unit.FromPoint(10);
        table.AddColumn(Unit.FromCentimeter(10));
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(3.5));

        foreach (var total in totals)
        {
            var row = table.AddRow();
            row.Cells[1].AddParagraph(total.Label);
            var amount = row.Cells[2].AddParagraph(total.Amount);
            amount.Format.Alignment = ParagraphAlignment.Right;

            if (total.IsGrandTotal)
            {
                row.Format.Font.Bold = true;
                row.Format.Font.Size = 11;
                row.Borders.Top.Color = Ink;
                row.Borders.Top.Width = 0.75;
            }
        }
    }
}
